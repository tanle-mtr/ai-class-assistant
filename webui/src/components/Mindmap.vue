<script>
import { h } from 'vue'

// 递归树节点（降级方案：无 CDN 时用 CSS 缩进树渲染，视觉贴近 markmap pill 风格）
const TreeNode = {
  name: 'TreeNode',
  props: { node: { type: Object, required: true } },
  render() {
    const cls = this.node.depth === 0 ? 'tn-root' : this.node.depth === 1 ? 'tn-mid' : 'tn-leaf'
    return h('div', { class: 'tn-branch' }, [
      h('div', { class: ['tn-node', cls] }, this.node.text),
      this.node.children && this.node.children.length
        ? h('div', { class: 'tn-children' }, this.node.children.map((c) => h(TreeNode, { node: c, key: c.text })))
        : null,
    ])
  },
}

export default { components: { TreeNode } }
</script>

<script setup>
import { computed, onMounted, onBeforeUnmount, ref } from 'vue'

const props = defineProps({
  content: { type: String, default: '' },
  title: { type: String, default: '思维导图' },
})

const svgRef = ref(null)
const cdnReady = ref(false)
const settled = ref(false)
let timer = null

// 从总结 markdown 中提取 markmap 代码块；没有就把正文当大纲解析
function extractMarkdown() {
  const m = props.content && props.content.match(/```(?:markmap|mindmap)?\s*\n([\s\S]*?)```/i)
  return m ? m[1] : props.content || ''
}

function parseTree(md) {
  const root = { text: '思维导图', depth: 0, children: [] }
  const lines = (md || '').split('\n').filter((l) => l.trim())
  if (!lines.length) return root

  const stack = [{ depth: -1, node: root }]
  let bulletBase = 1
  let first = true

  for (const raw of lines) {
    const indent = (raw.match(/^\s*/) || [''])[0].length
    const line = raw.trim()
    const heading = line.match(/^(#{1,6})\s+(.*)$/)
    const bullet = line.match(/^[-*+]\s+(.*)$/)
    let depth
    let text

    if (heading) {
      depth = heading[1].length - 1
      text = heading[2]
      bulletBase = depth + 1
    } else if (bullet) {
      depth = bulletBase + Math.floor(indent / 2)
      text = bullet[1]
    } else {
      depth = Math.max(1, stack[stack.length - 1].depth + 1)
      text = line
    }

    text = text.replace(/\*\*/g, '').replace(/`/g, '').replace(/#+$/, '').trim()
    if (!text) continue

    // 首个一级标题作为根节点
    if (first && depth === 0) {
      root.text = text
      first = false
      continue
    }
    first = false

    while (stack.length > 1 && stack[stack.length - 1].depth >= depth) stack.pop()
    const parent = stack[stack.length - 1].node
    const node = { text, depth, children: [] }
    parent.children.push(node)
    stack.push({ depth, node })
  }
  return root
}

const tree = computed(() => parseTree(extractMarkdown()))

function loadScript(src) {
  return new Promise((resolve, reject) => {
    if (document.querySelector(`script[data-mm="${src}"]`)) return resolve()
    const s = document.createElement('script')
    s.src = src
    s.async = true
    s.dataset.mm = src
    s.onload = resolve
    s.onerror = reject
    document.head.appendChild(s)
  })
}

onMounted(async () => {
  // CDN 4 秒超时：超时或渲染失败一律降级为缩进树
  timer = setTimeout(() => { settled.value = true }, 4000)
  try {
    await loadScript('https://cdn.jsdelivr.net/npm/markmap-view@0.3.11/dist/browser.min.js')
    await loadScript('https://cdn.jsdelivr.net/npm/markmap-lib@0.15.6/dist/browser.min.js')
    const mm = window.markmap
    if (mm && mm.Transformer && mm.Markmap && svgRef.value && extractMarkdown()) {
      const { root } = new mm.Transformer().transform(extractMarkdown())
      await mm.Markmap.create(svgRef.value, { duration: 300, fitPlusButton: false }, root)
      cdnReady.value = true
      settled.value = true
      clearTimeout(timer)
    }
  } catch (e) {
    console.info('[Mindmap] CDN 不可用，使用内置缩进树', e && e.message)
  }
})

onBeforeUnmount(() => { if (timer) clearTimeout(timer) })
</script>

<template>
  <div class="card mindmap-card">
    <div class="section-title">{{ title }}</div>
    <div class="mm-body">
      <svg v-show="cdnReady" ref="svgRef" class="mm-svg"></svg>
      <div v-show="!cdnReady" class="mm-fallback">
        <TreeNode :node="tree" />
      </div>
    </div>
    <div v-if="settled && !cdnReady" class="mm-tip">CDN 未加载，已切换为简易视图</div>
  </div>
</template>

<!-- 递归节点由 h() 动态创建，无法继承 scoped 属性，故用全局样式 + 专有前缀 -->
<style>
.mindmap-card { padding: 16px 18px; min-height: 190px; display: flex; flex-direction: column; }
.section-title { margin-bottom: 12px; }
.mm-body { flex: 1; min-height: 0; overflow: auto; display: flex; align-items: flex-start; }
.mm-svg { width: 100%; min-height: 180px; }
.mm-fallback { padding: 6px 2px; }
.mm-tip { font-size: 10px; color: #94A3B8; margin-top: 6px; text-align: right; }

.tn-branch { display: flex; align-items: center; }
.tn-node {
  border-radius: 999px;
  padding: 5px 12px;
  font-size: 12px;
  font-weight: 600;
  white-space: nowrap;
  line-height: 1.3;
  flex: 0 0 auto;
  box-shadow: 0 1px 3px rgba(15, 23, 42, .08);
}
.tn-root { background: var(--primary); color: #fff; font-size: 13px; }
.tn-mid { background: #CCFBF1; color: #0F766E; }
.tn-leaf { background: #F1F5F9; color: #475569; font-weight: 500; border: 1px solid #E2E8F0; }

.tn-children {
  display: flex;
  flex-direction: column;
  gap: 7px;
  margin-left: 22px;
  padding-left: 18px;
  position: relative;
}
.tn-children::before {
  content: '';
  position: absolute;
  left: 0;
  top: 8px;
  bottom: 8px;
  width: 1px;
  background: #CBD5E1;
}
.tn-children > .tn-branch { position: relative; }
.tn-children > .tn-branch::before {
  content: '';
  position: absolute;
  left: -18px;
  top: 50%;
  width: 18px;
  height: 1px;
  background: #CBD5E1;
}
</style>
