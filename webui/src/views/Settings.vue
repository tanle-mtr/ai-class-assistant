<script setup>
import { computed, onMounted, ref } from 'vue'
import api, { safe } from '../api.js'

const models = ref({ models: [], current: '', vision_models: [], hardware: {} })
const state = ref({})
const rules = ref([])
const publicIp = ref('')
const token = ref('')
const newModelName = ref('')
const baseModel = ref('')
const proxyPort = ref('')
const proxyName = ref('')
const msg = ref('')
const busy = ref(false)

const hardware = computed(() => models.value.hardware || {})

function notify(text) {
  msg.value = text
  setTimeout(() => { msg.value = '' }, 3000)
}

async function load() {
  const [m, s, r, ip] = await Promise.all([
    safe(api.getModels(), { models: [], current: '', vision_models: [], hardware: {} }),
    safe(api.getState(), {}),
    safe(api.getTunnelRules(), []),
    safe(api.getPublicIp(), { ip: '' }),
  ])
  models.value = m || { models: [], current: '', vision_models: [], hardware: {} }
  state.value = s || {}
  rules.value = Array.isArray(r) ? r : []
  publicIp.value = (ip && ip.ip) || ''
}

async function selectModel() {
  if (!models.value.current) return notify('请先选择一个模型')
  const d = await safe(api.selectModel(models.value.current), null)
  notify(d ? `主力模型已切换为 ${d.model || models.value.current}` : '切换失败：核心服务不可用')
}

async function createModel() {
  if (!newModelName.value.trim()) return notify('请输入模型名称')
  busy.value = true
  const d = await safe(api.createModel(newModelName.value.trim(), baseModel.value.trim() || undefined), null)
  busy.value = false
  if (d) {
    notify(d.msg || `模型 ${d.name} 创建成功`)
    newModelName.value = ''
    await load()
  } else notify('创建失败：核心服务不可用')
}

async function toggleMonitor(key) {
  const body = key === 'enabled'
    ? { enabled: !state.value.monitor_enabled }
    : { always_record: !state.value.monitor_always_record }
  const d = await safe(api.setMonitor(body), null)
  if (d) { state.value = { ...state.value, ...d }; notify('设置已保存') }
  else notify('保存失败：核心服务不可用')
}

async function saveToken() {
  const d = await safe(api.setTunnelToken(token.value.trim()), null)
  notify(d ? '隧道 Token 已保存' : '保存失败：核心服务不可用')
}

async function tunnelAction(kind) {
  busy.value = true
  const d = await safe(kind === 'start' ? api.startTunnel() : api.tunnelLogin(), null)
  busy.value = false
  if (d) {
    notify(d.msg || (kind === 'start' ? '隧道启动请求已发送' : '登录请求已发送'))
    if (kind === 'start' && d.url) window.open(d.url, '_blank', 'noopener')
  } else notify('操作失败：核心服务不可用')
}

async function addProxy() {
  const p = Number(proxyPort.value)
  if (!p || !proxyName.value.trim()) return notify('请填写端口和名称')
  const d = await safe(api.setTunnelProxy(p, proxyName.value.trim(), true), null)
  if (d) {
    notify('代理规则已保存')
    rules.value = Array.isArray(d.rules) ? d.rules : await safe(api.getTunnelRules(), [])
    proxyPort.value = ''
    proxyName.value = ''
  } else notify('保存失败：核心服务不可用')
}

async function shutdown() {
  if (!window.confirm('确定要退出 AI 课堂助手吗？将停止感知任务并导出当前数据。')) return
  const d = await safe(api.shutdown(), null)
  notify(d ? '核心服务正在退出' : '退出请求失败')
}

onMounted(load)
</script>

<template>
  <div class="settings">
    <div class="grid">
      <!-- 模型 -->
      <div class="card block">
        <div class="section-title">AI 模型</div>
        <div class="field-label">主力文本模型</div>
        <div class="form-row">
          <select v-model="models.current" class="field">
            <option value="" disabled>请选择模型</option>
            <option v-for="m in (models.models || [])" :key="m.name" :value="m.name">{{ m.name }}{{ m.size ? ` · ${m.size}` : '' }}</option>
          </select>
          <button class="btn btn-primary" :disabled="!models.current" @click="selectModel">应用</button>
        </div>
        <div v-if="(models.vision_models || []).length" class="hint">视觉模型：{{ (models.vision_models || []).join('、') }}</div>

        <div class="divider"></div>
        <div class="field-label">创建专属模型（内置技能与提示词）</div>
        <div class="form-row">
          <input v-model="newModelName" class="field" placeholder="新模型名称，如 classroom-zh" />
          <input v-model="baseModel" class="field" placeholder="基础模型（可选）" />
        </div>
        <button class="btn btn-outline block-btn" :disabled="busy" @click="createModel">创建模型</button>

        <div v-if="Object.keys(hardware).length" class="hw">
          <div class="section-title small-title">硬件探测</div>
          <div class="kv"><span>CPU</span><b>{{ hardware.cpu }} · {{ hardware.cores }} 核</b></div>
          <div class="kv"><span>内存</span><b>{{ hardware.ram_gb }} GB</b></div>
          <div class="kv"><span>GPU</span><b>{{ hardware.gpu || '无' }}{{ hardware.vram_gb ? ` · ${hardware.vram_gb} GB` : '' }}</b></div>
          <div v-if="hardware.advice" class="advice">{{ hardware.advice }}</div>
        </div>
      </div>

      <!-- 监控 -->
      <div class="card block">
        <div class="section-title">监控与隐私</div>
        <div class="switch-row">
          <div><div class="sw-title">开启监控</div><div class="sw-desc">上课自动录制摄像头与音频</div></div>
          <button class="switch" :class="{ on: state.monitor_enabled }" @click="toggleMonitor('enabled')"></button>
        </div>
        <div class="switch-row">
          <div><div class="sw-title">无人时仍录制</div><div class="sw-desc">默认关闭，仅有人上课才落盘</div></div>
          <button class="switch" :class="{ on: state.monitor_always_record }" @click="toggleMonitor('always')"></button>
        </div>
        <div class="kv-list">
          <div class="kv"><span>摄像头录制</span><b>{{ state.record_camera ? '开启' : '关闭' }}</b></div>
          <div class="kv"><span>音频录制</span><b>{{ state.record_audio ? '开启' : '关闭' }}</b></div>
          <div class="kv"><span>公网 IP</span><b>{{ publicIp || '未获取' }}</b></div>
          <div class="kv"><span>隧道</span><b>{{ state.tunnel ? (state.tunnel.enabled ? '已开启' : '已配置') : '未配置' }}</b></div>
        </div>
        <div class="danger">
          <button class="btn btn-outline danger-btn" @click="shutdown">退出助手</button>
        </div>
      </div>

      <!-- 隧道 -->
      <div class="card block wide">
        <div class="section-title">内网穿透（Cloudflare Tunnel）</div>
        <div class="field-label">Tunnel Token</div>
        <div class="form-row">
          <input v-model="token" class="field" type="password" placeholder="粘贴 Cloudflare 面板中的 Token" />
          <button class="btn btn-outline" @click="saveToken">保存</button>
        </div>
        <div class="form-row tunnel-actions">
          <button class="btn btn-primary" :disabled="busy" @click="tunnelAction('start')">启动隧道</button>
          <button class="btn btn-outline" :disabled="busy" @click="tunnelAction('login')">登录授权</button>
        </div>

        <div class="divider"></div>
        <div class="field-label">代理规则</div>
        <div class="form-row">
          <input v-model.number="proxyPort" class="field" type="number" placeholder="端口，如 5244" />
          <input v-model="proxyName" class="field" placeholder="名称，如 openlist" />
          <button class="btn btn-primary" @click="addProxy">添加</button>
        </div>
        <div class="rules">
          <div v-for="r in (rules || [])" :key="r.id" class="rule">
            <span class="rule-dot" :class="{ off: !r.enabled }"></span>
            <b>{{ r.name || `规则 ${r.id}` }}</b>
            <span class="muted">端口 {{ r.port }}</span>
            <a v-if="r.url" :href="r.url" target="_blank" rel="noopener" class="ellipsis">{{ r.url }}</a>
          </div>
          <div v-if="!rules.length" class="empty">暂无代理规则</div>
        </div>
      </div>
    </div>
    <div v-if="msg" class="toast">{{ msg }}</div>
  </div>
</template>

<style scoped>
.grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
.block { padding: 18px 20px; }
.wide { grid-column: 1 / -1; }
.field-label { font-size: 12px; color: var(--muted); margin: 12px 0 6px; font-weight: 600; }
.block-btn { width: 100%; margin-top: 10px; }
.hint { font-size: 12px; color: var(--muted); margin-top: 8px; }
.small-title { font-size: 13px; margin-bottom: 6px; }
.switch-row { display: flex; align-items: center; justify-content: space-between; padding: 12px 0; border-bottom: 1px solid var(--border); }
.sw-title { font-weight: 600; font-size: 13px; }
.sw-desc { color: var(--muted); font-size: 11px; }
.kv-list { margin-top: 6px; }
.kv { display: flex; justify-content: space-between; padding: 7px 0; border-bottom: 1px dashed var(--border); font-size: 13px; }
.kv span { color: var(--muted); }
.kv b { font-weight: 600; word-break: break-all; }
.hw { margin-top: 14px; background: #F8FAFC; border-radius: 8px; padding: 12px; }
.advice { color: var(--primary); font-size: 12px; margin-top: 8px; background: var(--soft); border-radius: 6px; padding: 8px; }
.danger { margin-top: 18px; text-align: right; }
.danger-btn { color: #DC2626; border-color: #FCA5A5; }
.tunnel-actions { margin-top: 10px; }
.rules { margin-top: 12px; display: flex; flex-direction: column; gap: 6px; }
.rule { display: flex; align-items: center; gap: 10px; padding: 8px 10px; border: 1px solid var(--border); border-radius: 8px; font-size: 12px; flex-wrap: wrap; }
.rule-dot { width: 8px; height: 8px; border-radius: 50%; background: #22C55E; }
.rule-dot.off { background: #94A3B8; }
.rule a { max-width: 280px; }
.toast { position: fixed; bottom: 24px; left: 50%; transform: translateX(-50%); background: var(--text); color: #fff; padding: 9px 18px; border-radius: 999px; font-size: 13px; z-index: 99; }
@media (max-width: 860px) { .grid { grid-template-columns: 1fr; } .wide { grid-column: auto; } }
</style>
