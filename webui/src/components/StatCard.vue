<script setup>
defineProps({
  label: { type: String, required: true },
  value: { type: [Number, String], default: 0 },
  unit: { type: String, default: '' },
  icon: { type: String, default: 'book' }, // book | alert | db | export
})
</script>

<template>
  <div class="card stat-card">
    <div class="stat-main">
      <div class="stat-value">
        {{ value }}<span v-if="unit" class="stat-unit">{{ unit }}</span>
      </div>
      <div class="stat-label">{{ label }}</div>
    </div>
    <div class="stat-icon" :class="`icon-${icon}`">
      <svg v-if="icon === 'book'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
        <path d="M4 5.5A2.5 2.5 0 0 1 6.5 3H12v16H6.5A2.5 2.5 0 0 0 4 21.5z"/>
        <path d="M20 5.5A2.5 2.5 0 0 0 17.5 3H12v16h5.5a2.5 2.5 0 0 1 2.5 2.5z"/>
        <path d="M8 7h2M8 11h2M14 7h2M14 11h2"/>
      </svg>
      <svg v-else-if="icon === 'alert'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
        <path d="M10.3 3.9 1.9 18a2 2 0 0 0 1.7 3h16.8a2 2 0 0 0 1.7-3L13.7 3.9a2 2 0 0 0-3.4 0z"/>
        <path d="M12 9v4M12 17h.01"/>
      </svg>
      <svg v-else-if="icon === 'db'" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
        <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/>
        <path d="M14 2v6h6M8 13l2.5-3 2.5 4 2-2.5 1.5 1.5"/>
      </svg>
      <svg v-else viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round">
        <path d="M4 7h16M4 12h16M4 17h10"/>
        <path d="M17 15l2 2-2 2"/>
        <rect x="3" y="3" width="18" height="18" rx="2"/>
      </svg>
    </div>
  </div>
</template>

<style scoped>
.stat-card {
  display: flex;
  align-items: center;
  justify-content: space-between;
  padding: 20px 20px;
  min-height: 104px;
  gap: 12px;
}
.stat-value {
  font-size: 32px;
  font-weight: 800;
  line-height: 1.1;
  color: var(--text);
  letter-spacing: -.5px;
}
.stat-unit { font-size: 15px; font-weight: 600; margin-left: 4px; color: var(--text-2); }
.stat-label { margin-top: 6px; font-size: 13px; color: var(--muted); }
.stat-icon {
  width: 48px;
  height: 48px;
  border-radius: 12px;
  display: grid;
  place-items: center;
  flex: 0 0 auto;
  background: var(--soft);
  color: var(--primary);
}
.stat-icon svg { width: 26px; height: 26px; }
.icon-alert { background: #F1F5F9; color: #94A3B8; }
.icon-db { background: #D8F3EE; color: #0D9488; }
.icon-export { background: #DCF5F1; color: #0F766E; }

@media (max-width: 640px) {
  .stat-card { min-height: 88px; padding: 14px; }
  .stat-value { font-size: 26px; }
  .stat-icon { width: 40px; height: 40px; }
}
</style>
