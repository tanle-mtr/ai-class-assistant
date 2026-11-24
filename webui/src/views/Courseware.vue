<script setup>
import { computed, onMounted, ref } from 'vue'
import api, { safe } from '../api.js'

const subjects = ref([])
const selectedSubject = ref('')
const uploadList = ref([])
const dragging = ref(false)
const fileInput = ref(null)
const newSubjectOpen = ref(false)
const newSubject = ref('')
const busy = ref(false)
const toast = ref('')

let uid = 0
const nextId = () => `u${++uid}`

function fmtSize(n) {
  if (!n && n !== 0) return ''
  if (n < 1024) return `${n} B`
  if (n < 1024 * 1024) return `${(n / 1024).toFixed(1)} KB`
  return `${(n / 1024 / 1024).toFixed(1)} MB`
}

function pickFiles() { if (!busy.value) fileInput.value && fileInput.value.click() }

function addFiles(files) {
  const arr = Array.from(files || [])
  if (!arr.length) return
  const items = arr.map((f) => ({
    id: nextId(),
    name: f.name,
    file: f,
    size: f.size,
    subject: selectedSubject.value || '未分类',
    progress: 0,
    status: 'pending',
  }))
  uploadList.value = [...items, ...uploadList.value]
}

function onDrop(e) {
  dragging.value = false
  addFiles(e.dataTransfer && e.dataTransfer.files)
}

function onInputChange(e) {
  addFiles(e.target.files)
  e.target.value = ''
}

function addSubject() {
  const name = newSubject.value.trim()
  if (!name) { newSubjectOpen.value = false; return }
  if (!subjects.value.includes(name)) subjects.value.push(name)
  selectedSubject.value = name
  newSubject.value = ''
  newSubjectOpen.value = false
}

const pendingCount = computed(() => uploadList.value.filter((x) => x.status === 'pending').length)

async function startUpload() {
  if (busy.value || !pendingCount.value) return
  busy.value = true
  toast.value = ''
  const items = uploadList.value.filter((x) => x.status === 'pending')
  for (const item of items) {
    item.status = 'uploading'
    item.progress = 0
    try {
      const res = await api.uploadCourseware(item.subject, item.file, (p) => { item.progress = p })
      item.progress = 100
      item.status = 'done'
      if (res && res.subject && !subjects.value.includes(res.subject)) subjects.value.push(res.subject)
      toast.value = `已上传 ${res && res.name ? res.name : item.name}`
    } catch (e) {
      item.status = 'error'
      toast.value = `${item.name} 上传失败：${e && e.message ? e.message : '未知错误'}`
    }
  }
  busy.value = false
  await loadCoursewareSilently()
}

async function loadCoursewareSilently() {
  const d = await safe(api.getCourseware(), {})
  const subjectsFromApi = (d && d.subjects) || []
  if (subjectsFromApi.length) {
    // 保留本地新建但后端尚未返回的科目
    subjects.value = Array.from(new Set([...subjectsFromApi, ...subjects.value]))
  }
}

onMounted(async () => {
  const d = await safe(api.getCourseware(), {})
  const data = d || {}
  subjects.value = Array.isArray(data.subjects) ? [...data.subjects] : []
  if (!selectedSubject.value && subjects.value.length) selectedSubject.value = subjects.value[0]
  const files = Array.isArray(data.files) ? data.files : []
  uploadList.value = files.map((f) => ({
    id: nextId(),
    name: f.name,
    subject: f.subject,
    size: f.size,
    progress: 100,
    status: 'done',
  }))
})
</script>

<template>
  <div class="courseware">
    <div class="panel card">
      <!-- 拖拽上传区：整块可点 -->
      <div
        class="dropzone"
        :class="{ dragging }"
        @click="pickFiles"
        @dragover.prevent="dragging = true"
        @dragenter.prevent="dragging = true"
        @dragleave.prevent="dragging = false"
        @drop.prevent="onDrop"
      >
        <div class="cloud">
          <svg viewBox="0 0 48 48" fill="none" stroke="currentColor" stroke-width="2.4" stroke-linecap="round" stroke-linejoin="round">
            <path d="M14 34a8 8 0 0 1-.7-15.98A11 11 0 0 1 34.6 20.2 7 7 0 0 1 34 34H14z" />
            <path d="M24 24v10M20 28l4-4 4 4" />
          </svg>
        </div>
        <div class="dz-title">拖拽课件到此处，或点击上传</div>
        <div class="dz-sub">支持 PDF、PPT、Word、图片，大小不限</div>
        <input ref="fileInput" type="file" multiple accept=".pdf,.ppt,.pptx,.doc,.docx,.png,.jpg,.jpeg,.txt" @change="onInputChange" />
      </div>

      <!-- 选择科目 -->
      <h3 class="block-title">选择科目</h3>
      <div class="subject-row">
        <div class="chips">
          <button
            v-for="s in subjects"
            :key="s"
            class="chip"
            :class="{ active: selectedSubject === s }"
            @click="selectedSubject = s"
          >{{ s }}</button>
          <div v-if="newSubjectOpen" class="new-subject-input">
            <input v-model="newSubject" class="field" placeholder="输入科目名称" maxlength="20" @keyup.enter="addSubject" @keyup.esc="newSubjectOpen = false" />
            <button class="btn btn-sm btn-primary" @click="addSubject">确定</button>
          </div>
        </div>
        <button v-if="!newSubjectOpen" class="btn btn-sm btn-outline add-subject" @click="newSubjectOpen = true">+ 新建科目</button>
      </div>

      <!-- 上传列表 -->
      <h3 class="block-title">上传列表</h3>
      <div class="upload-list">
        <div v-for="item in uploadList" :key="item.id" class="upload-row">
          <div class="doc-ico">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round">
              <path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z" /><path d="M14 2v6h6" />
            </svg>
          </div>
          <div class="u-name ellipsis">{{ item.name }}<span v-if="item.subject && item.status === 'done'" class="u-subject"> · {{ item.subject }}</span></div>
          <div class="progress">
            <div class="progress-track"><div class="progress-fill" :class="{ done: item.status === 'done', error: item.status === 'error' }" :style="{ width: item.progress + '%' }"></div></div>
          </div>
          <div class="u-status">
            <span v-if="item.status === 'done'" class="done-tag">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2.6" stroke-linecap="round" stroke-linejoin="round"><path d="M20 6 9 17l-5-5" /></svg>
              已完成
            </span>
            <span v-else-if="item.status === 'error'" class="err-tag">失败</span>
            <span v-else>{{ item.progress }}%</span>
          </div>
        </div>
        <div v-if="!uploadList.length" class="empty">暂无课件，拖拽文件到上方区域开始上传</div>
      </div>

      <p class="hint">课件将按科目自动分类，保存到教室电脑的课件文件夹，无需登录，访问密钥由老师分享。</p>
      <div class="submit-wrap">
        <button class="btn btn-primary btn-lg" :disabled="!pendingCount || busy" @click="startUpload">
          {{ busy ? '上传中…' : '开始上传' }}
        </button>
      </div>
      <div v-if="toast" class="toast">{{ toast }}</div>
    </div>
  </div>
</template>

<style scoped>
.panel { padding: 22px 24px 26px; }
.dropzone {
  border: 1.5px dashed #CBD5E1;
  border-radius: 12px;
  background: #FCFDFE;
  padding: 34px 16px;
  text-align: center;
  cursor: pointer;
  transition: all .18s ease;
}
.dropzone:hover, .dropzone.dragging { border-color: var(--accent); background: #F4FDFB; }
.dropzone input { display: none; }
.cloud {
  width: 52px; height: 52px; margin: 0 auto 10px; color: var(--accent);
  border-radius: 50%; display: grid; place-items: center; background: var(--soft);
}
.cloud svg { width: 34px; height: 34px; }
.dz-title { font-weight: 700; font-size: 15px; color: var(--text); }
.dz-sub { font-size: 12px; color: var(--muted); margin-top: 5px; }

.block-title { font-size: 14px; font-weight: 700; margin: 24px 0 12px; }
.subject-row { display: flex; align-items: flex-start; justify-content: space-between; gap: 12px; flex-wrap: wrap; }
.chips { display: flex; flex-wrap: wrap; gap: 9px; }
.add-subject { color: var(--primary); }
.new-subject-input { display: flex; gap: 6px; }
.new-subject-input .field { width: 132px; padding: 5px 10px; }

.upload-list { border: 1px solid var(--border); border-radius: 10px; overflow: hidden; }
.upload-row {
  display: flex; align-items: center; gap: 14px; padding: 11px 14px;
  border-bottom: 1px solid var(--border); background: #fff;
}
.upload-row:last-child { border-bottom: none; }
.doc-ico { width: 28px; height: 28px; flex: 0 0 auto; color: #94A3B8; }
.doc-ico svg { width: 24px; height: 24px; }
.u-name { flex: 0 0 210px; font-size: 13px; color: var(--text); }
.u-subject { color: var(--muted); font-size: 11px; }
.progress { flex: 1; min-width: 90px; }
.progress-track { height: 7px; background: #E9EEF3; border-radius: 999px; overflow: hidden; }
.progress-fill { height: 100%; background: linear-gradient(90deg, #14B8A6, #0D9488); border-radius: 999px; transition: width .2s; }
.progress-fill.done { background: #10B981; }
.progress-fill.error { background: #F87171; }
.u-status { width: 76px; text-align: right; font-size: 12px; color: var(--muted); flex: 0 0 auto; font-variant-numeric: tabular-nums; }
.done-tag { color: #10B981; display: inline-flex; align-items: center; gap: 3px; font-weight: 600; }
.done-tag svg { width: 14px; height: 14px; }
.err-tag { color: #EF4444; font-weight: 600; }

.hint { text-align: center; color: #94A3B8; font-size: 12px; margin: 18px 0 14px; }
.submit-wrap { text-align: center; }
.toast { text-align: center; margin-top: 10px; font-size: 12px; color: var(--primary); }

@media (max-width: 720px) {
  .panel { padding: 16px; }
  .u-name { flex: 1 1 140px; }
  .progress { flex: 1 1 100px; }
  .upload-row { gap: 8px; }
  .add-subject { margin-top: 8px; }
}
</style>
