<script setup>
// 今日课程时间轴：竖线 + 圆点 + 正在上课高亮气泡 + 距下课倒计时
defineProps({
  items: { type: Array, default: () => [] },
  currentSubject: { type: String, default: '' },
  countdown: { type: Number, default: 0 }, // 距下课分钟数
})
</script>

<template>
  <div class="card timeline-card">
    <div class="section-title">今日课程时间线</div>
    <div v-if="items.length" class="timeline">
      <div
        v-for="(item, i) in items"
        :key="i"
        class="tl-row"
        :class="{ active: item.subject === currentSubject }"
      >
        <div class="tl-time">{{ item.start }}</div>
        <div class="tl-rail">
          <span class="tl-dot"></span>
          <span v-if="i !== items.length - 1" class="tl-line"></span>
        </div>
        <div class="tl-body">
          <span class="tl-subject">{{ item.subject }}</span>
          <span v-if="item.subject === currentSubject" class="tl-live">
            正在上课 · 距下课 {{ countdown }} 分钟
          </span>
        </div>
      </div>
    </div>
    <div v-else class="empty">暂无课程安排</div>
  </div>
</template>

<style scoped>
.timeline-card { padding: 18px 20px 20px; }
.section-title { margin-bottom: 16px; }
.timeline { display: flex; flex-direction: column; }
.tl-row { display: flex; align-items: flex-start; min-height: 42px; position: relative; }
.tl-time { width: 48px; flex: 0 0 48px; color: var(--muted); font-size: 12px; padding-top: 2px; }
.tl-rail { width: 16px; flex: 0 0 16px; display: flex; flex-direction: column; align-items: center; align-self: stretch; position: relative; }
.tl-dot { width: 8px; height: 8px; border-radius: 50%; background: #CBD5E1; margin-top: 5px; flex: 0 0 auto; position: relative; z-index: 1; }
.tl-line { position: absolute; top: 13px; bottom: -2px; left: 50%; transform: translateX(-50%); width: 1px; background: #E2E8F0; }
.tl-body { display: flex; flex-direction: column; gap: 6px; padding-bottom: 12px; min-width: 0; }
.tl-subject { font-size: 13px; color: var(--text-2); font-weight: 500; }
.tl-row.active .tl-time { color: var(--primary); font-weight: 700; }
.tl-row.active .tl-dot { background: var(--primary); box-shadow: 0 0 0 4px var(--soft); width: 10px; height: 10px; margin-top: 4px; }
.tl-row.active .tl-subject { color: var(--text); font-weight: 700; }
.tl-live {
  display: inline-block;
  border: 1px solid var(--accent);
  color: var(--primary);
  background: #fff;
  border-radius: 8px;
  font-size: 12px;
  padding: 5px 12px;
  box-shadow: 0 2px 8px rgba(20, 184, 166, .12);
  width: fit-content;
}
</style>
