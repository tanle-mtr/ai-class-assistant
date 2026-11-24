<script setup>
import { computed, onMounted, onBeforeUnmount, provide, ref } from 'vue'
import api, { safe } from './api.js'
import Overview from './views/Overview.vue'
import Courseware from './views/Courseware.vue'
import Summary from './views/Summary.vue'
import Summaries from './views/Summaries.vue'
import Monitor from './views/Monitor.vue'
import Settings from './views/Settings.vue'
import Textbook from './views/Textbook.vue'
import Seatmap from './views/Seatmap.vue'

const TABS = [
  { key: 'overview', label: '总览' },
  { key: 'courseware', label: '课程' },
  { key: 'summaries', label: '总结文件' },
  { key: 'monitor', label: '监控' },
  { key: 'settings', label: '设置' },
  { key: 'textbook', label: '教科书' },
  { key: 'seatmap', label: '座位表' },
]

// 契约第 6 节：头部标题按页动态
const TITLES = {
  overview: '数据中心',
  courseware: '课件上传',
  summaries: '总结文件',
  summary: '课堂总结',
  monitor: '监控',
  settings: '设置',
  textbook: '教科书',
  seatmap: '座位表',
}

const VIEWS = {
  overview: Overview,
  courseware: Courseware,
  summaries: Summaries,
  summary: Summary,
  monitor: Monitor,
  settings: Settings,
  textbook: Textbook,
  seatmap: Seatmap,
}

function parseHash() {
  const raw = (window.location.hash || '').replace(/^#\/?/, '')
  const [path, qs] = raw.split('?')
  const params = {}
  if (qs) new URLSearchParams(qs).forEach((v, k) => { params[k] = v })
  return { view: VIEWS[path] ? path : 'overview', params }
}

const route = ref(parseHash())
const online = ref(false)

function onHashChange() { route.value = parseHash() }

function go(view, params = {}) {
  const qs = new URLSearchParams(params).toString()
  window.location.hash = `/${view}${qs ? '?' + qs : ''}`
}

const currentView = computed(() => route.value.view)
const currentComponent = computed(() => VIEWS[currentView.value] || Overview)
const pageTitle = computed(() => TITLES[currentView.value] || '数据中心')
const statusText = computed(() => (online.value ? '运行中' : '未连接'))

// 提供给子视图的导航与当前页参数
provide('go', go)
provide('routeParams', computed(() => route.value.params))

onMounted(async () => {
  window.addEventListener('hashchange', onHashChange)
  const st = await safe(api.getState(), null)
  online.value = !!st && typeof st === 'object'
})
onBeforeUnmount(() => window.removeEventListener('hashchange', onHashChange))
</script>

<template>
  <div class="app-shell">
    <header class="topbar">
      <div class="topbar-inner">
        <div class="brand" @click="go('overview')">
          <span class="brand-ai">AI</span>
          <span class="brand-name">课堂助手</span>
          <span class="brand-sep">·</span>
          <span class="brand-page">{{ pageTitle }}</span>
        </div>
        <nav class="tabs">
          <button
            v-for="tab in TABS"
            :key="tab.key"
            class="tab"
            :class="{ active: currentView === tab.key || (tab.key === 'summaries' && currentView === 'summary') }"
            @click="go(tab.key)"
          >{{ tab.label }}</button>
        </nav>
        <div class="badge-dot status" :class="{ offline: !online }">{{ statusText }}</div>
      </div>
    </header>

    <main class="content">
      <transition name="fade" mode="out-in">
        <component :is="currentComponent" :key="currentView" />
      </transition>
    </main>
  </div>
</template>

<style scoped>
.app-shell { min-height: 100vh; display: flex; flex-direction: column; }
.topbar { background: var(--header-bg); color: #fff; box-shadow: 0 1px 6px rgba(0,0,0,.12); flex: 0 0 auto; }
.topbar-inner {
  max-width: 1220px;
  margin: 0 auto;
  padding: 0 24px;
  height: 56px;
  display: flex;
  align-items: center;
  gap: 24px;
}
.brand { display: flex; align-items: center; gap: 7px; cursor: pointer; white-space: nowrap; flex: 0 0 auto; }
.brand-ai {
  display: inline-grid;
  place-items: center;
  background: rgba(255,255,255,.16);
  border: 1px solid rgba(255,255,255,.35);
  border-radius: 6px;
  font-size: 12px;
  font-weight: 800;
  padding: 2px 5px;
  letter-spacing: .3px;
}
.brand-name { font-size: 15px; font-weight: 700; }
.brand-sep { opacity: .5; margin: 0 2px; }
.brand-page { font-size: 15px; font-weight: 600; opacity: .96; }

.tabs { display: flex; align-items: stretch; gap: 2px; height: 100%; margin-left: auto; }
.tab {
  background: transparent;
  border: none;
  color: rgba(255,255,255,.75);
  font-size: 13px;
  padding: 0 12px;
  height: 100%;
  position: relative;
  white-space: nowrap;
  transition: color .15s;
}
.tab:hover { color: #fff; }
.tab.active { color: #fff; font-weight: 600; }
.tab.active::after {
  content: '';
  position: absolute;
  left: 10px;
  right: 10px;
  bottom: 0;
  height: 3px;
  border-radius: 3px 3px 0 0;
  background: #5EEAD4;
}

.status { color: #A7F3D0; white-space: nowrap; flex: 0 0 auto; font-size: 12px; }
.status.offline { color: #FCD34D; }
.status.offline::before { background: var(--amber); box-shadow: 0 0 0 3px rgba(245, 158, 11, .15); }

.content { flex: 1; width: 100%; max-width: 1220px; margin: 0 auto; padding: 22px 24px 40px; }

@media (max-width: 860px) {
  .topbar-inner { height: auto; flex-wrap: wrap; padding: 8px 16px; gap: 4px 12px; }
  .brand { height: 34px; }
  .tabs { width: 100%; order: 3; height: 38px; overflow-x: auto; margin-left: 0; }
  .tab { padding: 0 10px; }
  .status { margin-left: auto; }
  .content { padding: 16px; }
}
@media (max-width: 420px) {
  .brand-name { font-size: 14px; }
  .brand-page { font-size: 14px; }
  .tabs { flex-wrap: wrap; height: auto; overflow: visible; padding-bottom: 4px; }
  .tab { font-size: 12px; padding: 0 8px; height: 34px; }
  .content { padding: 12px; }
}
</style>
