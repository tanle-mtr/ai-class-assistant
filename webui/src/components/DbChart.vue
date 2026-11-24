<script setup>
import { computed } from 'vue'

// 分贝面积图：渐变填充 + 数据点 + 峰值橙点 + 平均虚线
const props = defineProps({
  title: { type: String, default: '课堂分贝统计（每分钟）' },
  samples: { type: Array, default: () => [] },
  avgDb: { type: [Number, String], default: 0 },
  peakDb: { type: [Number, String], default: 0 },
})

const W = 820
const H = 250
const PAD = { l: 54, r: 22, t: 18, b: 34 }
const plotW = W - PAD.l - PAD.r
const plotH = H - PAD.t - PAD.b
const baseY = PAD.t + plotH

const nums = computed(() =>
  props.samples.map((v) => (typeof v === 'number' && isFinite(v) ? v : 0))
)

const yMax = computed(() => {
  const peak = Math.max(props.peakDb || 0, ...nums.value, 0)
  return Math.max(80, Math.ceil((peak * 1.12) / 20) * 20)
})

const points = computed(() => {
  const n = nums.value.length
  if (!n) return []
  return nums.value.map((v, i) => ({
    x: n === 1 ? PAD.l + plotW / 2 : PAD.l + (i / (n - 1)) * plotW,
    y: baseY - (Math.min(v, yMax.value) / yMax.value) * plotH,
    v,
  }))
})

const linePath = computed(() =>
  points.value.map((p, i) => `${i ? 'L' : 'M'}${p.x.toFixed(1)},${p.y.toFixed(1)}`).join(' ')
)

const areaPath = computed(() => {
  const ps = points.value
  if (!ps.length) return ''
  return `${linePath.value} L${ps[ps.length - 1].x.toFixed(1)},${baseY} L${ps[0].x.toFixed(1)},${baseY} Z`
})

const avgY = computed(() =>
  baseY - (Math.min(Math.max(props.avgDb || 0, 0), yMax.value) / yMax.value) * plotH
)

const peakPoint = computed(() => {
  const ps = points.value
  if (!ps.length) return null
  return ps.reduce((a, b) => (b.v > a.v ? b : a), ps[0])
})

const yTicks = computed(() => {
  const step = yMax.value / 4
  return [0, 1, 2, 3, 4].map((i) => ({ y: baseY - (i / 4) * plotH, v: Math.round(i * step) }))
})

const xTicks = computed(() => {
  const n = nums.value.length
  if (n <= 1) return []
  const idx = [0, Math.round((n - 1) * 0.25), Math.round((n - 1) * 0.5), Math.round((n - 1) * 0.75), n - 1]
  return idx.map((i) => ({ x: PAD.l + (i / (n - 1)) * plotW, v: i }))
})

const avgText = computed(() => Number(props.avgDb || 0).toFixed(0))
const peakText = computed(() => Number(props.peakDb || 0).toFixed(0))
</script>

<template>
  <div class="card db-chart">
    <div class="db-head">
      <div class="section-title">{{ title }}</div>
      <div class="db-legend">
        平均 <b>{{ avgText }} dB</b><span class="dot-sep">·</span>峰值 <b class="peak">{{ peakText }} dB</b>
      </div>
    </div>

    <div v-if="!points.length" class="empty chart-empty">暂无分贝数据</div>
    <svg v-else class="chart" :viewBox="`0 0 ${W} ${H}`" preserveAspectRatio="xMidYMid meet">
      <defs>
        <linearGradient id="dbAreaFill" x1="0" y1="0" x2="0" y2="1">
          <stop offset="0%" stop-color="#14B8A6" stop-opacity=".38" />
          <stop offset="100%" stop-color="#14B8A6" stop-opacity=".02" />
        </linearGradient>
      </defs>

      <!-- 网格与 Y 轴刻度 -->
      <g v-for="t in yTicks" :key="`y${t.v}`">
        <line :x1="PAD.l" :x2="W - PAD.r" :y1="t.y" :y2="t.y" stroke="#EEF2F6" stroke-width="1" />
        <text :x="PAD.l - 10" :y="t.y + 4" text-anchor="end" font-size="12" fill="#94A3B8">{{ t.v }}</text>
      </g>
      <text :x="PAD.l - 10" :y="PAD.t - 6" text-anchor="end" font-size="11" fill="#94A3B8">dB</text>

      <!-- 面积 + 折线 -->
      <path :d="areaPath" fill="url(#dbAreaFill)" />
      <path :d="linePath" fill="none" stroke="#14B8A6" stroke-width="2.5" stroke-linejoin="round" stroke-linecap="round" />

      <!-- 平均虚线 -->
      <line :x1="PAD.l" :x2="W - PAD.r" :y1="avgY" :y2="avgY" stroke="#94A3B8" stroke-width="1.2" stroke-dasharray="6 5" />

      <!-- 数据点 -->
      <circle v-for="(p, i) in points" :key="i" :cx="p.x" :cy="p.y" r="3" fill="#fff" stroke="#14B8A6" stroke-width="2" />

      <!-- 峰值橙点 + 纵向辅助线 -->
      <g v-if="peakPoint">
        <line :x1="peakPoint.x" :x2="peakPoint.x" :y1="peakPoint.y" :y2="baseY" stroke="#F59E0B" stroke-width="1" stroke-dasharray="4 4" />
        <circle :cx="peakPoint.x" :cy="peakPoint.y" r="5.5" fill="#F97316" stroke="#fff" stroke-width="2" />
      </g>

      <!-- X 轴刻度 -->
      <g v-for="(t, i) in xTicks" :key="`x${i}`">
        <text :x="t.x" :y="baseY + 20" text-anchor="middle" font-size="12" fill="#94A3B8">{{ t.v }}</text>
      </g>
      <text :x="W - PAD.r" :y="baseY + 20" text-anchor="end" font-size="11" fill="#94A3B8">分钟</text>
    </svg>
  </div>
</template>

<style scoped>
.db-chart { padding: 18px 20px 14px; }
.db-head { display: flex; justify-content: space-between; align-items: center; gap: 12px; margin-bottom: 8px; flex-wrap: wrap; }
.db-legend { font-size: 12px; color: var(--muted); }
.db-legend b { color: var(--primary); font-weight: 700; }
.db-legend b.peak { color: var(--orange); }
.dot-sep { margin: 0 6px; color: #CBD5E1; }
.chart { width: 100%; height: auto; display: block; }
.chart-empty { padding: 40px 0; }
</style>
