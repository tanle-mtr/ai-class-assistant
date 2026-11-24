"""Cloudflare Tunnel 管理：令牌 / 登录 / 启动 / 任意端口代理规则。

代理规则保存于 data/config/tunnel.json（数组），并同步生成 cloudflared.yml
的 ingress 列表（末项 404 兜底），确保规则真正参与启动参数。
无 cloudflared / 无网时返回明确的 ok=False + msg，不抛异常、不 500。
"""
from __future__ import annotations

import json
import subprocess
import threading
from pathlib import Path
from typing import Any

from .. import get_logger
from ..config import config

log = get_logger("services.tunnel")

RULES_FILE = "tunnel.json"
YAML_FILE = "cloudflared.yml"


class TunnelManager:
    def __init__(self) -> None:
        self._dir = config.dir("config")
        self._rules_path = self._dir / RULES_FILE
        self._yml_path = self._dir / YAML_FILE
        self._exe = self._dir / "cloudflared.exe"
        self._proc: subprocess.Popen | None = None
        self._lock = threading.RLock()

    # ---------- 规则 ----------
    def _load_rules(self) -> list[dict[str, Any]]:
        if not self._rules_path.exists():
            return []
        try:
            data = json.loads(self._rules_path.read_text(encoding="utf-8"))
            if isinstance(data, list):
                return [item for item in data if isinstance(item, dict)]
            if isinstance(data, dict):
                # v1 格式 {name: {local_port, hostname, path}} → 契约数组格式
                converted = []
                for name, item in data.items():
                    if not isinstance(item, dict):
                        continue
                    port = int(item.get("local_port", 0) or 0)
                    converted.append({
                        "id": name, "name": name, "port": port,
                        "enabled": True,
                        "url": str(item.get("hostname", "") or ""),
                    })
                return converted
        except Exception as exc:
            log.warning(f"隧道规则读取失败（按空规则处理）: {exc}")
        return []

    def _save_rules(self, rules: list[dict[str, Any]]) -> None:
        try:
            self._rules_path.write_text(
                json.dumps(rules, ensure_ascii=False, indent=2), encoding="utf-8",
            )
            self.write_config(rules)
        except Exception as exc:
            log.warning(f"隧道规则保存失败: {exc}")

    def rules(self) -> list[dict[str, Any]]:
        return self._load_rules()

    def add_proxy(self, port: int, name: str, enabled: bool = True) -> list[dict[str, Any]]:
        """新增/更新一条端口代理规则，并立即刷新 cloudflared.yml。"""
        rules = self._load_rules()
        label = (name or "").strip() or "service"
        existing = next((item for item in rules if item.get("name") == label), None)
        domain = str(config.get("tunnel_domain", "") or "").strip().strip(".")
        url = f"https://{label}.{domain}" if domain else ""
        rule = {
            "id": str(existing.get("id") or label) if existing else label,
            "port": int(port),
            "name": label,
            "enabled": bool(enabled),
            "url": str(existing.get("url") or url) if existing else url,
        }
        if existing is not None:
            rules[rules.index(existing)] = rule
        else:
            rules.append(rule)
        self._save_rules(rules)
        log.info(f"隧道代理规则已保存: {label} -> :{port}（enabled={enabled}）")
        return self.rules()

    def write_config(self, rules: list[dict[str, Any]] | None = None) -> str:
        """生成 ingress 配置（仅启用规则），末项 404 兜底。"""
        rules = self.rules() if rules is None else rules
        lines: list[str] = []
        tunnel_id = str(config.get("tunnel_id", "") or "").strip()
        if tunnel_id:
            lines.append(f"tunnel: {tunnel_id}")
            credential = str(config.get("tunnel_credentials_file", "") or "").strip()
            if not credential:
                credential = str(Path.home() / ".cloudflared" / f"{tunnel_id}.json")
            lines.append(f"credentials-file: {credential}")
        lines.append("ingress:")
        for rule in rules:
            if not rule.get("enabled", True):
                continue
            port = int(rule.get("port", 0) or 0)
            if port <= 0:
                continue
            hostname = str(rule.get("url", "") or "").strip().replace("https://", "").replace("http://", "")
            lines.append(f"  - hostname: {hostname}" if hostname else "  - service: "
                         f"http://127.0.0.1:{port}")
            if hostname:
                lines.append(f"    service: http://127.0.0.1:{port}")
        lines.append("  - service: http_status:404")
        try:
            self._yml_path.write_text("\n".join(lines) + "\n", encoding="utf-8")
        except Exception as exc:
            log.warning(f"生成隧道配置失败: {exc}")
        return str(self._yml_path)

    # ---------- 令牌 / 登录 / 启动 ----------
    def set_token(self, token: str) -> dict[str, Any]:
        token = (token or "").strip()
        config.set("tunnel_token", token)
        config.set("tunnel_enabled", bool(token))
        return {"ok": True}

    def status(self) -> dict[str, Any]:
        running = self._proc is not None and self._proc.poll() is None
        return {
            "installed": self._exe.exists(),
            "running": running,
            "configured": bool(config.get("tunnel_token", "") or config.get("tunnel_id", "")),
            "rules": self.rules(),
            "yml_path": str(self._yml_path),
        }

    def start(self) -> dict[str, Any]:
        """启动隧道：有 token 用 --token；否则用已登录的命名隧道 + ingress 配置。"""
        if not self._exe.exists():
            return {"ok": False, "msg": "未找到 cloudflared.exe，请先安装 Cloudflare Tunnel 客户端"}
        token = str(config.get("tunnel_token", "") or "").strip()
        tunnel_id = str(config.get("tunnel_id", "") or "").strip()
        if not token and not tunnel_id:
            return {"ok": False, "msg": "请先粘贴 tunnel token，或点击「登录授权」配置命名隧道"}
        self.write_config()
        with self._lock:
            if self._proc is not None and self._proc.poll() is None:
                return {"ok": True, "msg": "隧道已在运行", "url": self._public_url()}
            command = (
                [str(self._exe), "tunnel", "run", "--token", token] if token
                else [str(self._exe), "--config", str(self._yml_path), "tunnel", "run", tunnel_id]
            )
            try:
                flags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
                self._proc = subprocess.Popen(
                    command, stdout=subprocess.PIPE, stderr=subprocess.STDOUT,
                    creationflags=flags, text=True,
                )
            except Exception as exc:
                log.warning(f"隧道启动失败: {exc}")
                self._proc = None
                return {"ok": False, "msg": f"启动失败：{exc}"}
        return {"ok": True, "msg": "隧道已启动", "url": self._public_url()}

    def login(self) -> dict[str, Any]:
        if not self._exe.exists():
            return {"ok": False, "msg": "未找到 cloudflared.exe，请先安装 Cloudflare Tunnel 客户端"}
        try:
            flags = getattr(subprocess, "CREATE_NO_WINDOW", 0)
            result = subprocess.run(
                [str(self._exe), "tunnel", "login"], capture_output=True, text=True,
                timeout=180, creationflags=flags,
            )
            if result.returncode == 0:
                return {"ok": True, "msg": "登录授权完成，请配置 tunnel_id 后启动命名隧道"}
            return {"ok": False, "msg": "登录未完成，请在弹出的浏览器中完成授权"}
        except subprocess.TimeoutExpired:
            return {"ok": False, "msg": "登录超时，若已完成浏览器授权可直接启动"}
        except Exception as exc:
            return {"ok": False, "msg": f"登录失败：{exc}"}

    def stop(self) -> None:
        with self._lock:
            if self._proc is not None and self._proc.poll() is None:
                try:
                    self._proc.terminate()
                except Exception:
                    pass
            self._proc = None

    def _public_url(self) -> str:
        for rule in self.rules():
            if rule.get("enabled", True) and rule.get("url"):
                return str(rule["url"])
        return str(config.get("tunnel_domain", "") or "")


tunnel = TunnelManager()
