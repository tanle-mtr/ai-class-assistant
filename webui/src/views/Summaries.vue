<script setup>
import { computed, inject, onMounted, ref } from 'vue'
import api, { safe } from '../api.js'

const go = inject('go', () => {})
const params = inject('routeParams', ref({}))
const days = ref([])

const visibleDays = computed(() => {
  const date = params.value && params.value.date
  if (!date) return days.value
  return days.value.filter((d) => d.date === date)
})

const total = computed(() =>
  visibleDays.value.reduce((n, d) => n + (d.courses || []).reduce((s, c) => s + (c.files || []).length, 0), 0)
)

function fmtSize(n) {
  if (!n && n !== 0) return ''
  return n < 1024 * 1024 ? `${(n / 1024 || 0).toFixed(0)} KB` : `${(n / 1024 / 1024).toFixed(1)} MB`
}

onMounted(async () => {
  const d = await safe(api.getSummaries(), [])
  days.value = Array.isArray(d) ? d : []
})
</script>

<template>
  <div class="summaries">
    <div class="card list-card">
      <div class="list-head">
        <div class="section-title">课堂总结文件</div>
        <span class="muted small">共 {{ total }} 个文件</span>
      </div>

      <div v-if="visibleDays.length" class="day-groups">
        <div v-for="day in visibleDays" :key="day.date" class="day-group">
          <div class="day-head">
            <svg viewBox="0 0 24 24" fill="currentColor"><path d="M2 6.5A1.5 1.5 0 0 1 3.5 5H9l2 2h9.5A1.5 1.5 0 0 1 22 8.5v9a1.5 1.5 0 0 1-1.5 1.5h-17A1.5 1.5 0 0 1 2 17.5z" /></svg>
            <span>{{ day.date }}</span>
          </div>
          <div v-for="course in (day.courses || [])" :key="course.name" class="course-group">
            <div class="course-name">{{ course.name }}</div>
            <div
              v-for="(f, i) in (course.files || [])"
              :key="i"
              class="file-row"
              @click="go('summary', { date: day.date, course: course.name, name: f.name })"
            >
              <div class="fico">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><path d="M14 2v6h6M8 13h8M8 17h6" /></svg>
              </div>
              <div class="fname ellipsis">{{ f.name }}</div>
              <span class="fsize muted">{{ fmtSize(f.size) }}</span>
              <button class="btn btn-sm btn-primary" @click.stop="go('summary', { date: day.date, course: course.name, name: f.name })">查看</button>
            </div>
          </div>
        </div>
      </div>
      <div v-else class="empty">暂无课堂总结文件</div>
    </div>
  </div>
</template>

<style scoped>
.list-card { padding: 20px 22px 24px; }
.list-head { display: flex; justify-content: space-between; align-items: center; margin-bottom: 16px; }
.small { font-size: 12px; }
.day-group { margin-bottom: 22px; }
.day-head { display: flex; align-items: center; gap: 8px; font-weight: 700; color: var(--text); margin-bottom: 10px; }
.day-head svg { width: 19px; height: 19px; color: #94A3B8; }
.course-group { margin-left: 26px; margin-bottom: 12px; }
.course-name { color: var(--primary); font-size: 13px; font-weight: 600; margin-bottom: 6px; }
.file-row {
  display: flex; align-items: center; gap: 12px; padding: 10px 12px;
  border: 1px solid var(--border); border-radius: 9px; margin-bottom: 8px;
  cursor: pointer; transition: border-color .15s, background .15s;
}
.file-row:hover { border-color: var(--accent); background: #F8FDFC; }
.fico { width: 26px; height: 26px; color: var(--primary); flex: 0 0 auto; }
.fico svg { width: 24px; height: 24px; }
.fname { flex: 1; font-size: 13px; font-weight: 500; }
.fsize { font-size: 11px; flex: 0 0 auto; }
@media (max-width: 420px) {
  .list-card { padding: 14px; }
  .course-group { margin-left: 8px; }
  .fsize { display: none; }
}
</style>
