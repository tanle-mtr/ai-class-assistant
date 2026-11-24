<script setup>
import { onMounted, ref } from 'vue'
import api, { safe } from '../api.js'
import Markdown from '../components/Markdown.vue'

const status = ref({ ready: false, chunks: 0, grade: '', region: '', books: [] })
const grade = ref('')
const region = ref('')
const books = ref([])
const question = ref('')
const answer = ref('')
const sources = ref([])
const fileInput = ref(null)
const msg = ref('')
const busy = ref(false)

function notify(t) { msg.value = t; setTimeout(() => { msg.value = '' }, 3000) }

async function load() {
  const s = await safe(api.getTextbookStatus(), {})
  status.value = s || {}
  grade.value = s.grade || ''
  region.value = s.region || ''
  books.value = Array.isArray(s.books) ? s.books : []
}

async function match() {
  if (!grade.value.trim()) return notify('请输入年级')
  busy.value = true
  const d = await safe(api.matchTextbook(grade.value.trim(), region.value.trim()), null)
  busy.value = false
  if (d) {
    books.value = Array.isArray(d.books) ? d.books : []
    notify(books.value.length ? `匹配到 ${books.value.length} 本教材` : '未匹配到可用教材，可导入 PDF')
  } else notify('匹配失败：核心服务不可用')
}

function pickPdf() { fileInput.value && fileInput.value.click() }

async function onPdf(e) {
  const f = e.target.files && e.target.files[0]
  e.target.value = ''
  if (!f) return
  busy.value = true
  const d = await safe(api.importTextbook(f), null)
  busy.value = false
  if (d) {
    notify(d.msg || `导入完成，共 ${d.chunks || 0} 个片段`)
    await load()
  } else notify('导入失败：核心服务不可用')
}

async function ask() {
  if (!question.value.trim()) return notify('请输入问题')
  busy.value = true
  const d = await safe(api.askTextbook(question.value.trim()), null)
  busy.value = false
  if (d) {
    answer.value = d.answer || '暂无答案'
    sources.value = Array.isArray(d.sources) ? d.sources : []
  } else notify('问答失败：核心服务不可用')
}

onMounted(load)
</script>

<template>
  <div class="textbook">
    <div class="grid">
      <div class="card block">
        <div class="section-title">教材匹配</div>
        <div class="status-line">
          <span class="badge-dot" :class="{ offline: !status.ready }">{{ status.ready ? '知识库就绪' : '知识库未就绪' }}</span>
          <span class="muted small">已索引 {{ status.chunks || 0 }} 个片段</span>
        </div>
        <div class="field-label">年级</div>
        <input v-model="grade" class="field" placeholder="如：高一" />
        <div class="field-label">地区 / 教材版本</div>
        <input v-model="region" class="field" placeholder="如：广东 人教版" />
        <button class="btn btn-primary block-btn" :disabled="busy" @click="match">匹配教材</button>

        <div class="divider"></div>
        <div class="field-label">教材清单</div>
        <div v-if="books.length" class="books">
          <div v-for="(b, i) in books" :key="i" class="book">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7"><path d="M4 4h7a3 3 0 0 1 3 3v13a3 3 0 0 0-3-3H4zM20 4h-3a3 3 0 0 0-3 3v13a3 3 0 0 1 3-3h3z" /></svg>
            <span>{{ b }}</span>
          </div>
        </div>
        <div v-else class="empty">暂无匹配教材</div>

        <div class="divider"></div>
        <button class="btn btn-outline block-btn" :disabled="busy" @click="pickPdf">导入教材 PDF</button>
        <input ref="fileInput" type="file" accept="application/pdf,.pdf" class="hidden-input" @change="onPdf" />
        <div class="hint">优先使用官方公开免费教材；无免费版时可自行导入 PDF 建立本地知识库。</div>
      </div>

      <div class="card block">
        <div class="section-title">教材问答</div>
        <div class="field-label">输入问题</div>
        <textarea v-model="question" class="field" rows="3" placeholder="如：二次函数的开口方向如何判断？"></textarea>
        <button class="btn btn-primary block-btn" :disabled="busy" @click="ask">提问</button>
        <div v-if="answer" class="answer">
          <div class="section-title small-title">回答</div>
          <Markdown :content="answer" />
          <div v-if="sources.length" class="sources-title">引用来源</div>
          <div v-for="(s, i) in sources" :key="i" class="source">
            {{ s.book || '教材' }} · 第 {{ s.page }} 页
            <span class="muted">相关度 {{ Number(s.score || 0).toFixed(2) }}</span>
          </div>
        </div>
        <div v-else class="empty">提问后这里显示基于教材的回答</div>
      </div>
    </div>
    <div v-if="msg" class="toast">{{ msg }}</div>
  </div>
</template>

<style scoped>
.grid { display: grid; grid-template-columns: 1fr 1fr; gap: 16px; }
.block { padding: 20px 22px; }
.status-line { display: flex; align-items: center; gap: 14px; margin: 12px 0 6px; }
.small { font-size: 12px; }
.status-line .offline::before { background: var(--amber); }
.field-label { font-size: 12px; color: var(--muted); font-weight: 600; margin: 12px 0 6px; }
.block-btn { width: 100%; margin-top: 12px; }
.books { display: flex; flex-direction: column; gap: 6px; }
.book { display: flex; align-items: center; gap: 8px; padding: 8px 10px; border: 1px solid var(--border); border-radius: 8px; font-size: 13px; }
.book svg { width: 18px; height: 18px; color: var(--primary); }
.hidden-input { display: none; }
.hint { font-size: 12px; color: var(--muted); margin-top: 10px; line-height: 1.6; }
.answer { margin-top: 16px; background: #F8FDFC; border: 1px solid #C7EBE5; border-radius: 10px; padding: 14px; }
.small-title { font-size: 13px; margin-bottom: 6px; }
.sources-title { font-size: 12px; font-weight: 700; margin: 12px 0 6px; }
.source { display: flex; justify-content: space-between; font-size: 12px; padding: 4px 0; border-bottom: 1px dashed var(--border); }
.toast { position: fixed; bottom: 24px; left: 50%; transform: translateX(-50%); background: var(--text); color: #fff; padding: 9px 18px; border-radius: 999px; font-size: 13px; z-index: 99; }
@media (max-width: 860px) { .grid { grid-template-columns: 1fr; } }
</style>
