<script setup>
import { computed } from 'vue'

// 自写极简 Markdown 渲染器：先转义 HTML 再解析，不引入第三方依赖
const props = defineProps({
  content: { type: String, default: '' },
})

function esc(s) {
  return String(s)
    .replace(/&/g, '&amp;')
    .replace(/</g, '&lt;')
    .replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;')
}

function safeUrl(u) {
  const v = u.trim()
  return /^(https?:\/\/|\/|#|\.\/|\.\.\/)/i.test(v) ? v : '#'
}

function inline(s) {
  return esc(s)
    .replace(/`([^`]+)`/g, '<code>$1</code>')
    .replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
    .replace(/(^|[^*])\*([^*]+)\*/g, '$1<em>$2</em>')
    .replace(/\[([^\]]+)\]\(([^)]+)\)/g, (_m, text, url) => `<a href="${safeUrl(url)}" target="_blank" rel="noopener">${text}</a>`)
}

const html = computed(() => {
  const lines = (props.content || '').split('\n')
  const out = []
  let i = 0
  let listType = null

  const closeList = () => {
    if (listType) { out.push(`</${listType}>`); listType = null }
  }
  const openList = (type) => {
    if (listType !== type) { closeList(); out.push(`<${type}>`); listType = type }
  }

  while (i < lines.length) {
    const line = lines[i]
    const t = line.trim()

    // 代码块
    if (/^```/.test(t)) {
      closeList()
      const lang = t.replace(/^```/, '').trim()
      i++
      const buf = []
      while (i < lines.length && !/^```/.test(lines[i].trim())) { buf.push(lines[i]); i++ }
      i++
      out.push(`<pre><code class="lang-${esc(lang)}">${esc(buf.join('\n'))}</code></pre>`)
      continue
    }

    // 标题
    const h = t.match(/^(#{1,6})\s+(.*)$/)
    if (h) {
      closeList()
      const lv = Math.min(h[1].length, 4)
      out.push(`<h${lv}>${inline(h[2])}</h${lv}>`)
      i++
      continue
    }

    // 引用
    if (/^>\s?/.test(t)) {
      closeList()
      out.push(`<blockquote>${inline(t.replace(/^>\s?/, ''))}</blockquote>`)
      i++
      continue
    }

    // 分割线
    if (/^(-{3,}|\*{3,})$/.test(t)) {
      closeList()
      out.push('<hr/>')
      i++
      continue
    }

    // 无序列表
    const ul = t.match(/^[-*+]\s+(.*)$/)
    if (ul) {
      openList('ul')
      out.push(`<li>${inline(ul[1])}</li>`)
      i++
      continue
    }

    // 有序列表
    const ol = t.match(/^\d+\.\s+(.*)$/)
    if (ol) {
      openList('ol')
      out.push(`<li>${inline(ol[1])}</li>`)
      i++
      continue
    }

    // 空行
    if (!t) {
      closeList()
      i++
      continue
    }

    // 普通段落：合并连续非空、非特殊行
    closeList()
    const buf = [t]
    i++
    while (i < lines.length) {
      const n = lines[i].trim()
      if (!n || /^(#{1,6})\s|^\s*[-*+]\s|^\d+\.\s|^```|^>/.test(n)) break
      buf.push(n)
      i++
    }
    out.push(`<p>${inline(buf.join(' '))}</p>`)
  }
  closeList()
  return out.join('')
})
</script>

<template>
  <div class="md" v-html="html"></div>
</template>

<style>
.md { font-size: 13px; line-height: 1.75; color: var(--text-2); word-break: break-word; }
.md h1, .md h2, .md h3, .md h4 { color: var(--text); font-weight: 700; margin: 12px 0 6px; }
.md h1 { font-size: 20px; }
.md h2 { font-size: 17px; }
.md h3 { font-size: 15px; }
.md h4 { font-size: 14px; }
.md p { margin: 6px 0; }
.md ul, .md ol { margin: 6px 0 6px 18px; }
.md li { margin: 3px 0; }
.md strong { color: var(--text); font-weight: 700; }
.md code { background: #F1F5F9; border-radius: 4px; padding: 1px 5px; font-size: 12px; color: #0F766E; }
.md pre { background: #0F172A; color: #E2E8F0; border-radius: 8px; padding: 12px; overflow-x: auto; margin: 8px 0; }
.md pre code { background: transparent; color: inherit; padding: 0; }
.md blockquote { border-left: 3px solid var(--accent); background: var(--soft); padding: 8px 12px; border-radius: 0 8px 8px 0; margin: 8px 0; }
.md hr { border: none; border-top: 1px solid var(--border); margin: 12px 0; }
.md a { color: var(--primary); text-decoration: underline; }
</style>
