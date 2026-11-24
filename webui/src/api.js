// API 封装：BASE 为空 = 与 Python 核心同源（18760 静态托管 dist）
// 所有函数返回 Promise；调用方用 safe() 或 try/catch 降级，后端未起时界面显示占位不崩溃。
const BASE = ''

function buildUrl(path, params) {
  let url = BASE + path
  if (params) {
    const q = new URLSearchParams()
    for (const k of Object.keys(params)) {
      const v = params[k]
      if (v !== undefined && v !== null && v !== '') q.set(k, v)
    }
    const s = q.toString()
    if (s) url += (url.includes('?') ? '&' : '?') + s
  }
  return url
}

async function http(path, { method = 'GET', body, form, params, raw = false } = {}) {
  const url = buildUrl(path, params)
  const opts = { method, headers: {} }
  if (form) {
    opts.body = form
  } else if (body !== undefined) {
    opts.headers['Content-Type'] = 'application/json'
    opts.body = JSON.stringify(body)
  }
  const res = await fetch(url, opts)
  if (raw) {
    if (!res.ok) throw new Error(`HTTP ${res.status}`)
    return res
  }
  const ct = res.headers.get('content-type') || ''
  let data
  if (ct.includes('application/json')) {
    data = await res.json()
  } else {
    data = await res.text()
  }
  if (!res.ok) {
    const err = new Error((data && data.error) || `HTTP ${res.status}`)
    err.data = data
    throw err
  }
  return data
}

function post(path, body) {
  return http(path, { method: 'POST', body: body || {} })
}

function fileForm(fields) {
  const fd = new FormData()
  for (const k of Object.keys(fields)) {
    const v = fields[k]
    if (v !== undefined && v !== null) fd.append(k, v)
  }
  return fd
}

// 带进度的 POST 上传（XMLHttpRequest 支持 upload.onprogress）
function uploadWithProgress(path, fields, onProgress) {
  return new Promise((resolve, reject) => {
    const xhr = new XMLHttpRequest()
    xhr.open('POST', BASE + path)
    xhr.upload.onprogress = (e) => {
      if (e.lengthComputable && onProgress) {
        onProgress(Math.min(99, Math.round((e.loaded / e.total) * 100)))
      }
    }
    xhr.onload = () => {
      let data
      try { data = JSON.parse(xhr.responseText) } catch { data = null }
      if (xhr.status >= 200 && xhr.status < 300) {
        if (onProgress) onProgress(100)
        resolve(data || { ok: true })
      } else {
        reject(new Error((data && data.error) || `HTTP ${xhr.status}`))
      }
    }
    xhr.onerror = () => reject(new Error('网络错误，无法连接核心服务'))
    xhr.send(fileForm(fields))
  })
}

export const api = {
  // ---------- 核心 ----------
  health: () => http('/api/health'),
  getState: () => http('/api/state'),
  getDb: () => http('/api/db'),
  getSchedule: () => http('/api/schedule'),
  setMonitor: (body) => post('/api/monitor', body),
  shutdown: () => post('/api/shutdown'),
  getPublicIp: () => http('/api/public_ip'),

  // ---------- AI ----------
  getModels: () => http('/api/models'),
  selectModel: (model) => post('/api/models/select', { model }),
  createModel: (name, base_model) => post('/api/models/create', { name, base_model }),
  summarize: () => post('/api/summarize'),
  exportNow: () => post('/api/export'),

  // ---------- 文件与课件 ----------
  getSummaries: () => http('/api/summaries'),
  fileDownloadUrl: (date, course, filename) =>
    buildUrl(`/api/files/${encodeURIComponent(date)}/${encodeURIComponent(course)}/${encodeURIComponent(filename)}`),
  getFileContent: (date, course, name) => http('/api/file/content', { params: { date, course, name } }),
  getCourseware: () => http('/api/courseware'),
  uploadCourseware: (subject, file, onProgress) =>
    uploadWithProgress('/api/upload', { subject, file }, onProgress),

  // ---------- 教科书 / 座位 / 隧道 ----------
  getTextbookStatus: () => http('/api/textbook/status'),
  matchTextbook: (grade, region) => post('/api/textbook/match', { grade, region }),
  importTextbook: (file) => http('/api/textbook/import', { method: 'POST', form: fileForm({ file }) }),
  askTextbook: (question) => post('/api/textbook/ask', { question }),
  getSeatmap: () => http('/api/seatmap'),
  initSeatmap: (file, class_name) =>
    http('/api/seatmap/init', { method: 'POST', form: fileForm({ file, class_name }) }),
  exportSeatmap: () => buildUrl('/api/seatmap/export'),
  setTunnelToken: (token) => post('/api/tunnel/token', { token }),
  startTunnel: () => post('/api/tunnel/start'),
  getTunnelRules: () => http('/api/tunnel/rules'),
  setTunnelProxy: (port, name, enabled) => post('/api/tunnel/proxy', { port, name, enabled }),
  tunnelLogin: () => post('/api/tunnel/login'),
}

// 统一降级助手：请求失败返回 fallback，保证界面不白屏
export async function safe(promise, fallback) {
  try {
    return await promise
  } catch (err) {
    console.warn('[api] 请求失败，使用占位数据：', err && err.message ? err.message : err)
    return fallback
  }
}

export default api
