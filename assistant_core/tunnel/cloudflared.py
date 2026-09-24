"""Cloudflare Tunnel（cloudflared）管理
- 自动下载安装 cloudflared（Windows x64）
- 两种启动方式：用户提供的 tunnel token（--token 方式，无需 VPS/公网 IP）；
  或 `cloudflared tunnel login` 授权后的命名隧道（--config cloudflared.yml run，ingress 规则生效）
- 隧道暴露：OpenList 端口、课件上传/控制台端口
- 提供"隧道代理"配置：用户可添加任意本地端口经隧道转发
  （规则保存于 tunnel.json，并同步生成 data/config/cloudflared.yml 的 ingress 列表）
"""
from __future__ import annotations

import json
import shutil
import subprocess
import threading
import time
from pathlib import Path

from ..config import config
from ..logger import get_logger

log = get_logger("tunnel.cloudflared")

CLOUDFLARED_URL = (
    "https://github.com/cloudflare/cloudflared/releases/latest/download/"
    "cloudflared-windows-amd64.exe"
)
CONFIG_FILE = "tunnel.json"
YAML_FILE = "cloudflared.yml"


class CloudflaredManager:
    def __init__(self):
        self._dir = config.dir("config")
        self._exe = self._dir / "cloudflared.exe"
        self._yml = self._dir / YAML_FILE
        self._proc: subprocess.Popen | None = None
        self._thread: threading.Thread | None = None
        self._lock = threading.Lock()

    # ---------- 安装 ----------
    def installed(self) -> bool:
        return self._exe.exists()

    def install(self) -> bool:
        """自动下载 cloudflared"""
        if self.installed():
            return True
        import requests
        log.info("正在下载 cloudflared…")
        try:
            r = requests.get(CLOUDFLARED_URL, timeout=120, stream=True)
            r.raise_for_status()
            tmp = self._exe.with_suffix(".tmp")
            with open(tmp, "wb") as f:
                for chunk in r.iter_content(8192):
                    f.write(chunk)
            tmp.rename(self._exe)
            log.info(f"cloudflared 安装完成: {self._exe}")
            return True
        except Exception as e:
            log.error(f"cloudflared 下载失败: {e}")
            return False

    # ---------- 配置 ----------
    def set_token(self, token: str):
        config.set("tunnel_token", token.strip())
        config.set("tunnel_enabled", bool(token.strip()))

    def get_token(self) -> str:
        return config.get("tunnel_token", "")

    # ---------- 代理规则（同时写入 cloudflared 的 ingress 配置） ----------
    def default_hostname(self, name: str) -> str:
        """默认主机名 <name>.<domain>；未配置 tunnel_domain 时返回空串"""
        domain = (config.get("tunnel_domain", "") or "").strip().strip(".")
        name = (name or "").strip()
        if not domain or not name:
            return ""
        return f"{name}.{domain}"

    def add_proxy(self, name: str, local_port: int, hostname: str = "", path: str = ""):
        """隧道代理：把本机端口/局域网服务经隧道暴露出去

        - 规则写入 tunnel.json（UI 展示）
        - 同时生成 cloudflared.yml 的 ingress（真正参与 cloudflared 启动参数）
        """
        rules = self._load_rules()
        rules[name] = {
            "local_port": int(local_port),
            "path": path or "",
            "hostname": (hostname or "").strip() or self.default_hostname(name),
        }
        self._save_rules(rules)
        self.write_config()
        host = rules[name]["hostname"]
        tip = host or "未配置域名，需在 Cloudflare 面板配置 Public Hostname"
        log.info(f"隧道代理已添加: {name} -> :{local_port}（{tip}）")

    def remove_proxy(self, name: str):
        rules = self._load_rules()
        rules.pop(name, None)
        self._save_rules(rules)
        self.write_config()
        log.info(f"隧道代理已移除: {name}")

    def yml_path(self) -> str:
        return str(self._yml)

    def write_config(self) -> str:
        """按规则生成 data/config/cloudflared.yml（ingress 列表，末项 404 兜底）"""
        rules = self._load_rules()
        tunnel_id = (config.get("tunnel_id", "") or "").strip()
        lines: list[str] = []
        if tunnel_id:
            lines.append(f"tunnel: {tunnel_id}")
            cred = (config.get("tunnel_credentials_file", "") or "").strip()
            if not cred:
                cred = str(Path.home() / ".cloudflared" / f"{tunnel_id}.json")
            lines.append(f"credentials-file: {cred}")
        lines.append("ingress:")
        for name, r in rules.items():
            if not isinstance(r, dict):
                continue
            port = int(r.get("local_port", 0) or 0)
            if not port:
                continue
            host = (r.get("hostname") or "").strip() or self.default_hostname(name)
            path = (r.get("path") or "").strip()
            first = True
            if host:
                lines.append(f"  - hostname: {host}")
                first = False
            if path:
                lines.append(f"{'  - ' if first else '    '}path: {path}")
                first = False
            lines.append(f"{'  - ' if first else '    '}service: http://127.0.0.1:{port}")
        # 兜底规则必须放最后
        lines.append("  - service: http_status:404")
        try:
            self._yml.write_text("\n".join(lines) + "\n", encoding="utf-8")
            log.info(f"已生成隧道配置: {self._yml}（{len(rules)} 条规则）")
        except Exception as e:
            log.error(f"写入隧道配置失败: {e}")
        return str(self._yml)

    def _load_rules(self) -> dict:
        p = self._dir / CONFIG_FILE
        if p.exists():
            try:
                return json.loads(p.read_text(encoding="utf-8"))
            except Exception:
                pass
        return {}

    def _save_rules(self, rules: dict):
        (self._dir / CONFIG_FILE).write_text(
            json.dumps(rules, ensure_ascii=False, indent=2), encoding="utf-8")

    def rules(self) -> dict:
        return self._load_rules()

    # ---------- 运行 ----------
    def start(self) -> bool:
        """启动隧道：
        - 有 tunnel token → `tunnel run --token`（面板管理的隧道）
        - 否则若已登录授权（tunnel_id / cert.pem）→ `tunnel --config <yml> run`（ingress 规则生效）
        """
        if not self.installed() and not self.install():
            log.error("cloudflared 不可用（未安装且下载失败）")
            return False
        token = self.get_token()
        tunnel_id = (config.get("tunnel_id", "") or "").strip()
        if not token and not tunnel_id:
            log.warning("未配置 tunnel token，也未配置 tunnel_id：请在控制台点击「登录授权」（cloudflared tunnel login）后再启动")
            return False
        # 每次启动前刷新 ingress 配置，保证规则生效
        try:
            self.write_config()
        except Exception as e:
            log.warning(f"生成隧道配置失败: {e}")

        with self._lock:
            if self._proc and self._proc.poll() is None:
                return True
            if token:
                cmd = [str(self._exe), "tunnel", "run", "--token", token]
                log.info("启动 Cloudflare Tunnel（token 模式；ingress 规则请在 Cloudflare 面板的 Public Hostname 配置）…")
            else:
                cmd = [str(self._exe), "--config", str(self._yml), "tunnel", "run", tunnel_id]
                log.info(f"启动 Cloudflare Tunnel（命名隧道 {tunnel_id}，本地 ingress 规则生效）…")
            try:
                self._proc = subprocess.Popen(
                    cmd,
                    stdout=subprocess.PIPE,
                    stderr=subprocess.STDOUT,
                    creationflags=subprocess.CREATE_NO_WINDOW,
                    text=True,
                )
            except Exception as e:
                log.error(f"cloudflared 启动失败: {e}")
                self._proc = None
                return False
        self._thread = threading.Thread(target=self._tail, daemon=True)
        self._thread.start()
        return True

    def login(self) -> bool:
        """引导 `cloudflared tunnel login`：打开浏览器授权，生成 ~/.cloudflared/cert.pem"""
        if not self.installed() and not self.install():
            log.error("cloudflared 不可用（未安装且下载失败）")
            return False
        log.info("正在引导 cloudflared 登录授权…（请在弹出的浏览器中完成授权）")
        try:
            r = subprocess.run(
                [str(self._exe), "tunnel", "login"],
                capture_output=True, text=True, timeout=180,
                creationflags=subprocess.CREATE_NO_WINDOW,
            )
            out = (r.stdout or "") + ("\n" + r.stderr if r.stderr else "")
            for line in out.splitlines()[-15:]:
                if line.strip():
                    log.info(f"[cloudflared] {line.strip()[:200]}")
            if r.returncode == 0:
                log.info("cloudflared 登录授权完成，可在控制台配置 tunnel_id 后启动命名隧道")
                return True
            log.warning(f"cloudflared 登录未完成（返回码 {r.returncode}）")
            return False
        except subprocess.TimeoutExpired:
            log.warning("cloudflared 登录超时（180s）；若已在浏览器完成授权，可直接配置 tunnel_id 启动")
            return False
        except Exception as e:
            log.error(f"cloudflared 登录失败: {e}")
            return False

    def stop(self):
        with self._lock:
            if self._proc and self._proc.poll() is None:
                self._proc.terminate()
            self._proc = None

    def _tail(self):
        if not self._proc or not self._proc.stdout:
            return
        for line in self._proc.stdout:
            line = line.strip()
            if line:
                log.info(f"[cloudflared] {line[:200]}")
            if self._proc.poll() is not None:
                break

    def status(self) -> dict:
        running = self._proc is not None and self._proc.poll() is None
        return {
            "installed": self.installed(),
            "running": running,
            "configured": bool(self.get_token()),
            "rules": self.rules(),
            "yml_path": self.yml_path(),
            "tunnel_id": config.get("tunnel_id", ""),
            "domain": config.get("tunnel_domain", ""),
        }


cloudflared = CloudflaredManager()
