<script setup>
import { computed, onMounted, ref, inject } from 'vue'
import api, { safe } from '../api.js'
import StatCard from '../components/StatCard.vue'
import Timeline from '../components/Timeline.vue'

const go = inject('go', () => {})

const state = ref({})
const db = ref({})
const schedule = ref({ date: '', items: [], source: '' })
const summaries = ref([])
const now = ref(new Date())

function minutesOf(t) {
  const m = /^(\d{1,2}):(\d{2})/.exec(t || '')
  return m ? Number(m[1]) * 60 + Number(m[2]) : -1
}

const activeItem = computed(() => {
  const cur = now.value.getHours() * 60 + now.value.getMinutes()
  const sessionSubject = state.value && state.value.session && state.value.session.subject
  const list = schedule.value.items || []
  if (sessionSubject) {
    const hit = list.find((i) => i.subject === sessionSubject)
    if (hit) return hit
  }
  return list.find((i) => {
    const s = minutesOf(i.start)
    const e = minutesOf(i.end)
    return s >= 0 && e >= s && cur >= s && cur < e
  }) || null
})

const countdown = computed(() => {
  if (!activeItem.value) return 0
  const cur = now.value.getHours() * 60 + now.value.getMinutes()
  return Math.max(0, minutesOf(activeItem.value.end) - cur)
})

const overtimeCount = computed(() => {
  const s = state.value || {}
  return Number(s.overtime_count ?? (s.session && s.session.overtime_count) ?? 0) || 0
})

const avgDb = computed(() => Number((db.value && db.value.avg_db) || 0))
const exportFileCount = computed(() =>
  summaries.value.reduce(
    (sum, d) => sum + (d.courses || []).reduce((n, c) => n + (c.files || []).length, 0),
    0
  )
)

const summaryRows = computed(() => {
  const rows = []
  for (const day of summaries.value || []) {
    for (const c of day.courses || []) {
      for (const f of c.files || []) {
        const hit = (schedule.value.items || []).find((i) => i.subject === c.name)
        rows.push({
          date: day.date,
          course: c.name,
          name: f.name,
          size: f.size,
          url: f.url,
          time: hit ? `${hit.start}-${hit.end}` : '',
          label: String(f.name || '').replace(/\.(md|markdown|txt)$/i, ''),
        })
      }
    }
  }
  return rows.slice(0, 4)
})

const exportDates = computed(() => (summaries.value || []).map((d) => d.date).filter(Boolean))

let timer = null
onMounted(async () => {
  timer = setInterval(() => { now.value = new Date() }, 30000)
  const [s, d, sc, su] = await Promise.all([
    safe(api.getState(), {}),
    safe(api.getDb(), {}),
    safe(api.getSchedule(), { date: '', items: [], source: '' }),
    safe(api.getSummaries(), []),
  ])
  state.value = s || {}
  db.value = d || {}
  schedule.value = sc || { date: '', items: [], source: '' }
  summaries.value = Array.isArray(su) ? su : []
  now.value = new Date()
})
</script>

<template>
  <div class="overview">
    <div class="stats-grid">
      <StatCard label="今日课程" :value="(schedule.items || []).length" icon="book" />
      <StatCard label="拖堂次数" :value="overtimeCount" icon="alert" />
      <StatCard label="平均分贝" :value="avgDb" unit="dB" icon="db" />
      <StatCard label="已导出文件" :value="exportFileCount" icon="export" />
    </div>

    <div class="mid-grid">
      <Timeline :items="schedule.items || []" :current-subject="activeItem ? activeItem.subject : ''" :countdown="countdown" />

      <div class="card summary-card">
        <div class="section-title">课堂总结</div>
        <div v-if="summaryRows.length" class="summary-list">
          <div v-for="(r, i) in summaryRows" :key="i" class="summary-item">
            <div class="file-ico" :class="{ alt: i % 2 === 1 }">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round">
                <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" />
                <path d="M14 2v6h6M8 13h8M8 17h6" />
              </svg>
            </div>
            <div class="summary-info">
              <div class="summary-name ellipsis">{{ r.course }} · {{ r.label }}</div>
              <div class="summary-meta">{{ r.time }}<span v-if="r.time"> · </span>已生成</div>
            </div>
            <button class="btn btn-sm view-btn" @click="go('summary', { date: r.date, course: r.course, name: r.name })">查看</button>
          </div>
        </div>
        <div v-else class="empty">暂无课堂总结</div>
      </div>
    </div>

    <div class="card export-card">
      <div class="section-title">导出文件（OpenList）</div>
      <div v-if="exportDates.length" class="folder-row">
        <button v-for="(d, i) in exportDates" :key="i" class="folder" @click="go('summaries', { date: d })">
          <svg viewBox="0 0 24 24" fill="currentColor">
            <path d="M2 6.5A1.5 1.5 0 0 1 3.5 5H9l2 2h9.5A1.5 1.5 0 0 1 22 8.5v9a1.5 1.5 0 0 1-1.5 1.5h-17A1.5 1.5 0 0 1 2 17.5z" opacity=".95" />
          </svg>
          <span>{{ d }}</span>
        </button>
      </div>
      <div v-else class="empty">暂无导出文件</div>
    </div>
  </div>
</template>

<style scoped>
.stats-grid { display: grid; grid-template-columns: repeat(4, minmax(0, 1fr)); gap: 16px; margin-bottom: 16px; }
.mid-grid { display: grid; grid-template-columns: minmax(0, 1fr) minmax(0, 1fr); gap: 16px; margin-bottom: 16px; }

.summary-card { padding: 18px 20px 20px; }
.summary-card .section-title { margin-bottom: 12px; }
.summary-list { display: flex; flex-direction: column; gap: 10px; }
.summary-item {
  display: flex;
  align-items: center;
  gap: 12px;
  padding: 10px 12px;
  border: 1px solid var(--border);
  border-radius: 10px;
  background: #fff;
}
.file-ico {
  width: 34px; height: 34px; border-radius: 9px; flex: 0 0 auto;
  display: grid; place-items: center; background: var(--soft); color: var(--primary);
}
.file-ico svg { width: 19px; height: 19px; }
.file-ico.alt { background: #E0F2FE; color: #0284C7; }
.summary-info { flex: 1; min-width: 0; }
.summary-name { font-weight: 600; font-size: 13px; color: var(--text); }
.summary-meta { font-size: 11px; color: var(--muted); margin-top: 2px; }
.view-btn { background: var(--primary); color: #fff; border-color: var(--primary); }

.export-card { padding: 18px 20px 22px; }
.export-card .section-title { margin-bottom: 16px; }
.folder-row { display: flex; justify-content: space-around; gap: 16px; flex-wrap: wrap; }
.folder {
  background: transparent; border: none; display: flex; flex-direction: column;
  align-items: center; gap: 8px; color: #94A3B8; font-size: 12px; padding: 4px 12px;
  border-radius: 8px; transition: color .15s, background .15s;
}
.folder:hover { color: var(--primary); background: var(--soft); }
.folder svg { width: 34px; height: 34px; }

@media (max-width: 960px) {
  .stats-grid { grid-template-columns: repeat(2, minmax(0, 1fr)); }
  .mid-grid { grid-template-columns: 1fr; }
}
@media (max-width: 420px) {
  .stats-grid { grid-template-columns: 1fr 1fr; gap: 10px; }
  .summary-item { padding: 8px; gap: 8px; }
  .view-btn { padding: 4px 10px; }
}
</style>
