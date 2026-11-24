<script setup>
import { computed, onMounted, ref } from 'vue'
import api, { safe } from '../api.js'

const seatmap = ref({ class_name: '', seats: [] })
const fileInput = ref(null)
const msg = ref('')
const busy = ref(false)

const seats = computed(() => seatmap.value.seats || [])
const rows = computed(() => {
  const map = {}
  for (const s of seats.value) {
    const r = Number(s.row) || 1
    if (!map[r]) map[r] = []
    map[r].push(s)
  }
  return Object.keys(map).sort((a, b) => Number(a) - Number(b)).map((k) => ({ row: Number(k), seats: map[k].sort((a, b) => (Number(a.col) || 0) - (Number(b.col) || 0)) }))
})

function notify(t) { msg.value = t; setTimeout(() => { msg.value = '' }, 3000) }

async function load() {
  const d = await safe(api.getSeatmap(), {})
  seatmap.value = d || { class_name: '', seats: [] }
}

function pickImage() { fileInput.value && fileInput.value.click() }

async function onImage(e) {
  const f = e.target.files && e.target.files[0]
  e.target.value = ''
  if (!f) return
  busy.value = true
  const d = await safe(api.initSeatmap(f, seatmap.value.class_name || ''), null)
  busy.value = false
  if (d) {
    notify(`识别完成，共 ${Array.isArray(d.seats) ? d.seats.length : 0} 个座位`)
    await load()
  } else notify('识别失败：核心服务不可用')
}

function exportSeatmap() {
  window.open(api.exportSeatmap(), '_blank')
}

onMounted(load)
</script>

<template>
  <div class="seatmap">
    <div class="card block">
      <div class="head-row">
        <div>
          <div class="section-title">座位表</div>
          <div class="muted small">{{ seatmap.class_name || '未设置班级' }} · 共 {{ seats.length }} 个座位</div>
        </div>
        <div class="head-actions">
          <button class="btn btn-outline" :disabled="busy" @click="pickImage">{{ busy ? '识别中…' : '上传图片初始化' }}</button>
          <button class="btn btn-primary" @click="exportSeatmap">导出座位表</button>
          <input ref="fileInput" type="file" accept="image/*" class="hidden-input" @change="onImage" />
        </div>
      </div>

      <div v-if="rows.length" class="seat-board">
        <div v-for="r in rows" :key="r.row" class="seat-row">
          <div class="row-label">{{ r.row }} 排</div>
          <div
            v-for="s in r.seats"
            :key="s.id"
            class="seat"
            :class="{ recognized: !!s.name }"
            :title="s.name || `座位 ${s.id}`"
          >
            <div class="seat-name">{{ s.name || '空位' }}</div>
            <div class="seat-id">{{ s.col }} 列{{ s.face_id ? ' · 已建档' : '' }}</div>
          </div>
        </div>
        <div class="podium">讲台</div>
      </div>
      <div v-else class="empty">暂无座位数据，上传座位表图片以初始化</div>
    </div>
    <div v-if="msg" class="toast">{{ msg }}</div>
  </div>
</template>

<style scoped>
.block { padding: 20px 22px 26px; }
.head-row { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-bottom: 18px; flex-wrap: wrap; }
.small { font-size: 12px; margin-top: 2px; }
.head-actions { display: flex; gap: 8px; }
.hidden-input { display: none; }
.seat-board { background: #F8FAFC; border: 1px solid var(--border); border-radius: 12px; padding: 22px; overflow-x: auto; }
.seat-row { display: flex; align-items: center; gap: 10px; margin-bottom: 12px; flex-wrap: wrap; }
.row-label { width: 40px; flex: 0 0 40px; color: var(--muted); font-size: 12px; }
.seat {
  width: 96px; min-height: 56px; border: 1px solid var(--border); border-radius: 9px;
  background: #fff; display: flex; flex-direction: column; align-items: center; justify-content: center;
  box-shadow: var(--shadow); padding: 6px;
}
.seat.recognized { border-color: #99DCD3; background: #F2FBF9; }
.seat-name { font-size: 13px; font-weight: 600; color: var(--text); }
.seat-id { font-size: 10px; color: var(--muted); margin-top: 2px; }
.podium { margin: 20px auto 0; width: 160px; text-align: center; background: var(--header-bg); color: #fff; padding: 7px; border-radius: 8px; font-size: 13px; letter-spacing: 4px; }
.toast { position: fixed; bottom: 24px; left: 50%; transform: translateX(-50%); background: var(--text); color: #fff; padding: 9px 18px; border-radius: 999px; font-size: 13px; z-index: 99; }
@media (max-width: 420px) { .block { padding: 14px; } .seat { width: 76px; } }
</style>
