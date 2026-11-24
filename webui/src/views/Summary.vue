<script setup>
import { computed, inject, onMounted, ref } from 'vue'
import api, { safe } from '../api.js'
import Markdown from '../components/Markdown.vue'
import Mindmap from '../components/Mindmap.vue'
import DbChart from '../components/DbChart.vue'

const params = inject('routeParams', ref({}))
const content = ref('')
const db = ref({})
const loading = ref(true)

function splitSections(md) {
  const buckets = {}
  let cur = '_top'
  buckets[cur] = []
  for (const line of (md || '').split('\n')) {
    const h = line.match(/^\s*#{1,3}\s+(.*)$/)
    if (h) {
      cur = h[1].replace(/[*#]/g, '').trim()
      if (!buckets[cur]) buckets[cur] = []
    } else {
      if (!buckets[cur]) buckets[cur] = []
      buckets[cur].push(line)
    }
  }
  const find = (re) => {
    for (const k of Object.keys(buckets)) {
      if (re.test(k)) {
        const v = buckets[k].join('\n').trim()
        if (v) return v
      }
    }
    return ''
  }
  return {
    teacher: find(/老师总结|教师总结|课堂质量|教学质量/),
    student: find(/学生总结|本节重点|本节难点|重难点/),
    mind: find(/思维导图|mindmap|知识图谱/i),
  }
}

const sections = computed(() => splitSections(content.value))

const mmContent = computed(() => {
  const m = content.value.match(/```(?:markmap|mindmap)?\s*\n([\s\S]*?)```/i)
  return m ? m[1].trim() : sections.value.mind
})

const rating = computed(() => {
  const t = sections.value.teacher || ''
  let n = (t.match(/★/g) || []).length
  if (!n) n = (t.match(/⭐/g) || []).length
  if (!n) {
    const m = t.match(/(\d)\s*\/\s*5/) || t.match(/质量[^\d]{0,6}(\d)/)
    if (m) n = Math.min(5, Number(m[1]))
  }
  return n
})

const suggestions = computed(() => {
  const t = sections.value.teacher || ''
  return t
    .split('\n')
    .map((l) => l.trim().replace(/^\s*[-*+]\s+/, '').replace(/^#{1,6}\s+/, ''))
    .filter((l) => l && /(建议细讲|可缩短|防拖堂|细讲|缩短|拖堂)/.test(l))
    .slice(0, 5)
})

// 建议与星级已单独呈现，正文里去掉这些行避免重复
const teacherRest = computed(() => {
  const t = sections.value.teacher || ''
  return t
    .split('\n')
    .filter((l) => {
      const s = l.trim().replace(/^\s*[-*+]\s+/, '').replace(/^#{1,6}\s+/, '')
      if (/课堂质量|教学质量/.test(s) && /★|\d/.test(s)) return false
      if (suggestions.value.some((v) => s === v || s.includes(v))) return false
      return true
    })
    .join('\n')
    .trim()
})

const rawName = computed(() => String(params.value.name || '课堂记录').replace(/\.(md|markdown|txt)$/i, ''))

const cleanName = computed(() => {
  const c = params.value.course || ''
  const n = rawName.value
  return c && n.startsWith(c) ? n.slice(c.length).replace(/^[·\s\-—]+/, '') : n
})

const displayTitle = computed(() => {
  const c = params.value.course || '课堂'
  return `课堂总结 · ${c} ${cleanName.value}`
})

const meta = computed(() => {
  const m = content.value.match(/[^\n]*\d{1,2}:\d{2}[^\n]*/)
  if (m && m[0].includes('·')) return m[0].trim()
  const bits = [params.value.className || '', params.value.time || '', params.value.date || ''].filter(Boolean)
  return bits.length ? bits.join(' · ') : `来源：${cleanName.value}`
})

onMounted(async () => {
  const p = params.value || {}
  const [c, d] = await Promise.all([
    safe(
      p.date && p.course && p.name
        ? api.getFileContent(p.date, p.course, p.name)
        : Promise.resolve({ name: '', content: '' }),
      { name: '', content: '' }
    ),
    safe(api.getDb(), {}),
  ])
  content.value = (c && c.content) || ''
  db.value = d || {}
  loading.value = false
})
</script>

<template>
  <div class="summary-view">
    <div class="card head">
      <h1 class="title">{{ displayTitle }}</h1>
      <div class="meta">{{ meta }}</div>
    </div>

    <div class="tri-grid">
      <!-- 老师总结 -->
      <div class="card col">
        <div class="section-title">老师总结</div>
        <div class="col-body">
          <div class="quality">
            <span class="q-label">课堂质量</span>
            <span v-if="rating" class="stars">
              <span v-for="i in 5" :key="i" :class="i <= rating ? 'on' : ''">★</span>
            </span>
            <span v-else class="muted small">暂无评价</span>
          </div>
          <div class="advice">
            <div v-for="(s, i) in suggestions" :key="i" class="advice-line">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5" /></svg>
              <span>{{ s }}</span>
            </div>
            <div v-if="!suggestions.length" class="muted small">暂无教学建议</div>
          </div>
          <div v-if="teacherRest" class="more"><Markdown :content="teacherRest" /></div>
        </div>
      </div>

      <!-- 学生总结 -->
      <div class="card col">
        <div class="section-title">学生总结</div>
        <div class="col-body student">
          <Markdown v-if="sections.student" :content="sections.student" />
          <div v-else class="empty">暂无学生总结</div>
        </div>
      </div>

      <!-- 思维导图 -->
      <Mindmap :content="mmContent" />
    </div>

    <DbChart
      class="chart-block"
      :samples="(db && db.samples) || []"
      :avg-db="(db && db.avg_db) || 0"
      :peak-db="(db && db.peak_db) || 0"
    />
  </div>
</template>

<style scoped>
.head { padding: 22px 26px; margin-bottom: 16px; }
.title { font-size: 26px; font-weight: 800; color: var(--text); letter-spacing: -.3px; }
.meta { margin-top: 6px; color: var(--muted); font-size: 13px; }
.tri-grid { display: grid; grid-template-columns: repeat(3, minmax(0, 1fr)); gap: 16px; margin-bottom: 16px; }
.col { padding: 16px 18px; min-height: 190px; display: flex; flex-direction: column; }
.col .section-title { padding-bottom: 10px; border-bottom: 1px solid var(--border); }
.col-body { flex: 1; padding-top: 12px; }
.quality { display: flex; align-items: center; gap: 10px; margin-bottom: 10px; }
.q-label { font-weight: 700; font-size: 13px; color: var(--text); }
.stars { color: var(--accent); letter-spacing: 2px; font-size: 17px; line-height: 1; }
.stars span { color: #CBD5E1; }
.stars span.on { color: #F59E0B; }
.small { font-size: 12px; }
.advice { border: 1px dashed #A7DCD4; background: #F2FBF9; border-radius: 8px; padding: 8px 10px; display: flex; flex-direction: column; gap: 6px; }
.advice-line { display: flex; align-items: flex-start; gap: 6px; font-size: 12px; color: #0F766E; line-height: 1.45; }
.advice-line svg { width: 14px; height: 14px; flex: 0 0 auto; margin-top: 1px; color: #10B981; }
.more { margin-top: 10px; }
.student :deep(.md ul) { margin-left: 16px; }
.chart-block { width: 100%; }

@media (max-width: 980px) {
  .tri-grid { grid-template-columns: 1fr; }
}
@media (max-width: 420px) {
  .title { font-size: 20px; }
  .head { padding: 16px; }
}
</style>
