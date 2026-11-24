<script setup>
import { computed, onMounted, ref } from 'vue'
import api, { safe } from '../api.js'
import DbChart from '../components/DbChart.vue'

const state = ref({})
const db = ref({})
const message = ref('')
const busy = ref(false)

const session = computed(() => state.value.session || null)
const monitorEnabled = computed(() => !!state.value.monitor_enabled)
const alwaysRecord = computed(() => !!state.value.monitor_always_record)

const sessionText = computed(() => {
  const s = session.value
  if (!s) return '当前无进行中的课程'
  return `${s.subject || '未命名课程'} · ${s.start || ''}${s.end ? ' - ' + s.end : ''}`
})

async function toggle(which) {
  const body = which === 'always'
    ? { always_record: !alwaysRecord.value }
    : { enabled: !monitorEnabled.value }
  const d = await safe(api.setMonitor(body), null)
  if (d) {
    state.value = { ...state.value, ...d }
    message.value = which === 'always' ? '已更新无人上课录制策略' : (d.monitor_enabled ? '监控已开启' : '监控已关闭')
  } else {
    message.value = '操作失败：核心服务不可用'
  }
  setTimeout(() => { message.value = '' }, 2600)
}

async function act(kind) {
  busy.value = true
  const p = kind === 'summarize' ? api.summarize() : api.exportNow()
  const d = await safe(p, null)
  message.value = d ? (kind === 'summarize' ? '已触发生成总结' : '已触发导出') : '操作失败：核心服务不可用'
  busy.value = false
  setTimeout(() => { message.value = '' }, 2600)
}

onMounted(async () => {
  const [s, d] = await Promise.all([safe(api.getState(), {}), safe(api.getDb(), {})])
  state.value = s || {}
  db.value = d || {}
})
</script>

<template>
  <div class="monitor">
    <div class="top-grid">
      <div class="card status-card">
        <div class="section-title">课堂状态</div>
        <div class="big-status">
          <span class="pulse" :class="{ on: monitorEnabled }"></span>
          {{ monitorEnabled ? '监控运行中' : '监控已关闭' }}
        </div>
        <div class="session-line">{{ sessionText }}</div>
        <div class="privacy">隐私策略：{{ alwaysRecord ? '始终录制' : '仅在有人上课时录制' }}</div>
        <div class="switch-row">
          <div>
            <div class="sw-title">开启监控</div>
            <div class="sw-desc">麦克风 / 摄像头 / 屏幕感知</div>
          </div>
          <button class="switch" :class="{ on: monitorEnabled }" @click="toggle('enabled')"></button>
        </div>
        <div class="switch-row">
          <div>
            <div class="sw-title">无人时也录制</div>
            <div class="sw-desc">默认关闭，保护隐私</div>
          </div>
          <button class="switch" :class="{ on: alwaysRecord }" @click="toggle('always')"></button>
        </div>
      </div>

      <div class="card status-card">
        <div class="section-title">当前会话</div>
        <div v-if="session" class="kv-list">
          <div class="kv"><span>课程</span><b>{{ session.subject || '—' }}</b></div>
          <div class="kv"><span>班级</span><b>{{ session.class_name || state.class_name || '—' }}</b></div>
          <div class="kv"><span>开始</span><b>{{ session.start || '—' }}</b></div>
          <div class="kv"><span>预计结束</span><b>{{ session.end || '—' }}</b></div>
          <div class="kv"><span>拖堂监测</span><b :class="session.overtime ? 'warn' : 'ok'">{{ session.overtime ? '检测到拖堂' : '正常' }}</b></div>
        </div>
        <div v-else class="empty">暂无进行中的课程</div>
        <div class="divider"></div>
        <div class="actions">
          <button class="btn btn-primary" :disabled="busy" @click="act('summarize')">生成总结</button>
          <button class="btn btn-outline" :disabled="busy" @click="act('export')">导出课堂</button>
        </div>
      </div>
    </div>

    <DbChart
      :samples="(db && db.samples) || []"
      :avg-db="(db && db.avg_db) || 0"
      :peak-db="(db && db.peak_db) || 0"
      title="实时分贝"
    />
    <div v-if="message" class="toast">{{ message }}</div>
  </div>
</template>

<style scoped>
.top-grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; margin-bottom: 16px; }
.status-card { padding: 18px 20px; }
.big-status { display: flex; align-items: center; gap: 9px; font-size: 20px; font-weight: 800; margin: 14px 0 4px; }
.pulse { width: 11px; height: 11px; border-radius: 50%; background: #94A3B8; flex: 0 0 auto; }
.pulse.on { background: #22C55E; box-shadow: 0 0 0 5px rgba(34,197,94,.14); }
.session-line { color: var(--text-2); font-size: 13px; }
.privacy { color: var(--muted); font-size: 12px; margin-top: 4px; }
.switch-row { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-top: 16px; }
.sw-title { font-weight: 600; font-size: 13px; }
.sw-desc { color: var(--muted); font-size: 11px; margin-top: 2px; }
.kv-list { margin-top: 12px; }
.kv { display: flex; justify-content: space-between; padding: 7px 0; border-bottom: 1px dashed var(--border); font-size: 13px; }
.kv span { color: var(--muted); }
.kv b { font-weight: 600; }
.kv b.ok { color: #16A34A; }
.kv b.warn { color: #F59E0B; }
.actions { display: flex; gap: 10px; }
.toast { position: fixed; bottom: 24px; left: 50%; transform: translateX(-50%); background: var(--text); color: #fff; padding: 9px 18px; border-radius: 999px; font-size: 13px; z-index: 99; }
@media (max-width: 860px) { .top-grid { grid-template-columns: 1fr; } }
</style>
