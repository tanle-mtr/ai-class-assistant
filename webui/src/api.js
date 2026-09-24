// API 封装（BASE 为空 = 与页面同源；调用方负责 try/catch，后端不可用时降级）
const BASE = ''

async function j(url, opts) {
  const r = await fetch(BASE + url, opts)
  if (!r.ok) throw new Error(`HTTP ${r.status}`)
  return r.json()
}

function postJson(url, body) {
  return j(url, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(body || {}),
  })
}

function qs(params) {
  const u = new URLSearchParams()
  for (const k in params || {}) u.append(k, params[k] == null ? '' : params[k])
  return u.toString()
}

export default {
  // ---------- 状态 / 分贝 ----------
  state: () => j('/api/state'),
  db: () => j('/api/db'),
  schedule: () => j('/api/schedule'),
  publicIp: () => j('/api/public_ip'),

  // ---------- 模型 ----------
  models: () => j('/api/models'),
  selectModel: (model) => postJson('/api/models/select', { model }),
  createModel: (name, base_model) => postJson('/api/models/create', { name, base_model }),

  // ---------- 总结 / 文件 ----------
  summarize: () => j('/api/summarize', { method: 'POST' }),
  exportNow: () => j('/api/export', { method: 'POST' }),
  summaries: () => j('/api/summaries'),
  fileContent: (date, course, name) =>
    j(`/api/file/content?${qs({ date, course, name })}`),

  // ---------- 课件 ----------
  courseware: () => j('/api/courseware'),
  upload: (file, subject) => {
    const fd = new FormData()
    fd.append('file', file)
    fd.append('subject', subject || '')
    return j('/api/upload', { method: 'POST', body: fd })
  },

  // ---------- 教科书知识库 ----------
  textbookStatus: () => j('/api/textbook/status'),
  textbookMatch: (grade, region) => postJson('/api/textbook/match', { grade, region }),
  textbookImport: (file, title) => {
    const fd = new FormData()
    fd.append('file', file)
    fd.append('title', title || '')
    return j('/api/textbook/import', { method: 'POST', body: fd })
  },
  textbookAsk: (question, top_k) => postJson('/api/textbook/ask', { question, top_k }),

  // ---------- 座位表 ----------
  seatmap: () => j('/api/seatmap'),
  initSeatmap: (file, class_name) => {
    const fd = new FormData()
    fd.append('file', file)
    fd.append('class_name', class_name || '')
    return j('/api/seatmap/init', { method: 'POST', body: fd })
  },
  exportSeatmap: () => j('/api/seatmap/export'),

  // ---------- 隧道 ----------
  tunnelRules: () => j('/api/tunnel/rules'),
  setToken: (token) => postJson('/api/tunnel/token', { token }),
  startTunnel: () => j('/api/tunnel/start', { method: 'POST' }),
  tunnelLogin: () => j('/api/tunnel/login', { method: 'POST' }),
  addProxy: (name, port, hostname) => postJson('/api/tunnel/proxy', { name, port, hostname }),
}
