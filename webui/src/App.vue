<template>
  <div class="app">
    <!-- ================= 顶部导航（深青实色，效果图 02/03/04 一致） ================= -->
    <header class="topbar">
      <div class="brand">
        <span class="brand-badge">
          <svg viewBox="0 0 24 24" width="19" height="19" fill="none" stroke="currentColor"
               stroke-width="1.9" stroke-linecap="round" stroke-linejoin="round">
            <path d="M4 5.5A2.5 2.5 0 0 1 6.5 3H19v14H6.5A2.5 2.5 0 0 0 4 19.5z"/>
            <path d="M4 19.5A2.5 2.5 0 0 1 6.5 17H19v4H6.5A2.5 2.5 0 0 1 4 19.5z"/>
          </svg>
        </span>
        <span class="brand-name">AI课堂助手</span>
        <span class="brand-sep">·</span>
        <span class="brand-page">{{ pageTitle }}</span>
      </div>
      <nav class="nav">
        <button v-for="t in tabs" :key="t.key" :class="['nav-btn', { active: tab === t.key }]"
                @click="tab = t.key">{{ t.label }}</button>
      </nav>
      <span v-if="pageStatus" class="top-status"><i class="dot"></i>{{ pageStatus }}</span>
    </header>

    <main class="content">
      <!-- ================= 总览（效果图 02） ================= -->
      <section v-if="tab === 'overview'" class="page">
        <div class="stat-grid">
          <div class="stat-card">
            <div class="stat-body">
              <div class="num">{{ todayCount }}</div>
              <div class="lbl">今日课程</div>
            </div>
            <span class="stat-ico">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"
                   stroke-linecap="round" stroke-linejoin="round">
                <rect x="3.4" y="5.2" width="17.2" height="15.4" rx="2.6"/>
                <path d="M8 3.2v4M16 3.2v4M3.4 10.2h17.2"/>
                <path d="M7.8 14h2.2M13.9 14h2.3M7.8 17.2h2.2"/>
              </svg>
            </span>
          </div>
          <div class="stat-card">
            <div class="stat-body">
              <div class="num">{{ overtimeCount }}</div>
              <div class="lbl">拖堂次数</div>
            </div>
            <span class="stat-ico warn">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"
                   stroke-linecap="round" stroke-linejoin="round">
                <circle cx="12" cy="12.6" r="8.4"/>
                <path d="M12 8v5l3.2 2"/>
                <path d="M8.6 2.6 6 4.9M15.4 2.6 18 4.9"/>
              </svg>
            </span>
          </div>
          <div class="stat-card">
            <div class="stat-body">
              <div class="num">{{ avgDb }}<small>dB</small></div>
              <div class="lbl">平均分贝</div>
            </div>
            <span class="stat-ico">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9"
                   stroke-linecap="round" stroke-linejoin="round">
                <path d="M3.6 10.4v3.2M7.8 6.6v11M12 3.8v16.4M16.2 7.4v9.2M20.4 10.6v2.8"/>
              </svg>
            </span>
          </div>
          <div class="stat-card">
            <div class="stat-body">
              <div class="num">{{ exportCount }}</div>
              <div class="lbl">已导出文件</div>
            </div>
            <span class="stat-ico">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"
                   stroke-linecap="round" stroke-linejoin="round">
                <path d="M13.2 3.2H7.6A2.4 2.4 0 0 0 5.2 5.6v12.8a2.4 2.4 0 0 0 2.4 2.4h8.8a2.4 2.4 0 0 0 2.4-2.4V9z"/>
                <path d="M13.2 3.2V9h5.6"/>
                <path d="M9.2 14.4h5.6M9.2 17.2h3.4"/>
              </svg>
            </span>
          </div>
        </div>

        <div class="two-col">
          <!-- 今日课程时间线 -->
          <div class="card">
            <div class="card-head">
              <h3 class="card-title">今日课程时间线</h3>
            </div>
            <div class="timeline">
              <div class="tl-row" v-for="(c, i) in scheduleItems" :key="i" :class="c.state">
                <span class="tl-time">{{ c.start }}</span>
                <span class="tl-subject">{{ c.subject }}</span>
                <i v-if="c.state === 'now'" class="tl-now-dot"></i>
                <span v-if="c.state === 'now'" class="tl-bubble">正在上课 · 距下课 {{ minutesLeft }} 分钟</span>
              </div>
              <div v-if="!scheduleItems.length" class="empty">等待 ClassIsland 同步课表…</div>
            </div>
          </div>

          <!-- 课堂总结 -->
          <div class="card">
            <div class="card-head">
              <h3 class="card-title">课堂总结</h3>
            </div>
            <div class="sum-list">
              <div class="sum-item" v-for="(s, i) in summaryCards" :key="s.range + s.name">
                <span class="sum-ico">
                  <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"
                       stroke-linecap="round" stroke-linejoin="round">
                    <path d="M13.2 3.2H7.6A2.4 2.4 0 0 0 5.2 5.6v12.8a2.4 2.4 0 0 0 2.4 2.4h8.8a2.4 2.4 0 0 0 2.4-2.4V9z"/>
                    <path d="M13.2 3.2V9h5.6M8.8 13h6.4M8.8 16.4h4"/>
                  </svg>
                </span>
                <div class="sum-info">
                  <div class="sum-name">{{ s.name }}</div>
                  <div class="sum-meta">{{ s.range }} · 已生成</div>
                </div>
                <button class="btn sm" :class="{ ghost: i > 0 }" @click="openSummary(s)">查看</button>
              </div>
              <div v-if="!summaryCards.length" class="empty">还没有课堂总结</div>
            </div>
          </div>
        </div>

        <!-- 导出文件（OpenList） -->
        <div class="card">
          <div class="card-head">
            <h3 class="card-title">导出文件（OpenList）</h3>
          </div>
          <div class="dir-row">
            <div class="dir-item" v-for="d in exportDays" :key="d.date" @click="openExport(d)">
              <span class="dir-ico">
                <svg viewBox="0 0 24 24" fill="currentColor">
                  <path d="M3.2 6.4a2.2 2.2 0 0 1 2.2-2.2h3.6l1.9 2.1h7.7a2.2 2.2 0 0 1 2.2 2.2v9.1a2.2 2.2 0 0 1-2.2 2.2H5.4a2.2 2.2 0 0 1-2.2-2.2z"/>
                </svg>
              </span>
              <span class="dir-date">{{ d.date }}</span>
            </div>
            <div v-if="!exportDays.length" class="empty">暂无导出记录</div>
          </div>
        </div>
      </section>

      <!-- ================= 课程 · 课件上传（效果图 03） ================= -->
      <section v-if="tab === 'courseware'" class="page">
        <div class="drop-zone" :class="{ over: dragOver }" @click="$refs.file.click()"
             @dragover.prevent="dragOver = true" @dragleave.prevent="dragOver = false"
             @drop.prevent="onDrop">
          <span class="dz-ico">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.7"
                 stroke-linecap="round" stroke-linejoin="round">
              <path d="M6.5 18.5a4 4 0 0 1-.5-7.97A5.4 5.4 0 0 1 16.6 9.2a3.6 3.6 0 0 1 .4 7.16"/>
              <path d="M12 11.6v6.9"/>
              <path d="M9.3 14.3 12 11.6l2.7 2.7"/>
            </svg>
          </span>
          <div class="dz-title">拖拽课件到此处，或点击上传</div>
          <div class="dz-hint">支持 PDF、PPT、Word、图片，大小不限</div>
          <input type="file" ref="file" multiple hidden @change="onPick" />
        </div>

        <div class="subjects">
          <div class="subj-label">选择科目</div>
          <div class="subj-row">
            <button v-for="s in subjects" :key="s" class="chip" :class="{ on: subject === s }"
                    @click="subject = s">{{ s }}</button>
            <button class="chip add" @click="newSubject">
              <svg viewBox="0 0 24 24" width="13" height="13" fill="none" stroke="currentColor"
                   stroke-width="2.4" stroke-linecap="round"><path d="M12 5.5v13M5.5 12h13"/></svg>
              新建科目
            </button>
            <span v-if="!subjects.length" class="empty">暂无科目，请新建</span>
          </div>
        </div>

        <div class="card">
          <div class="card-head">
            <h3 class="card-title">上传列表</h3>
            <span class="card-note" v-if="uploads.length">共 {{ uploads.length }} 项</span>
          </div>
          <div class="ul-row" v-for="u in uploads" :key="u.name">
            <span class="ul-ico">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"
                   stroke-linecap="round" stroke-linejoin="round">
                <path d="M13.2 3.2H7.6A2.4 2.4 0 0 0 5.2 5.6v12.8a2.4 2.4 0 0 0 2.4 2.4h8.8a2.4 2.4 0 0 0 2.4-2.4V9z"/>
                <path d="M13.2 3.2V9h5.6"/>
              </svg>
            </span>
            <div class="ul-main">
              <div class="ul-name">{{ u.name }}</div>
              <div class="ul-bar"><i :style="{ width: (u.done ? 100 : (u.pct || 0)) + '%' }"></i></div>
            </div>
            <span class="ul-state" :class="{ done: u.done }">
              <template v-if="u.done">已完成</template>
              <template v-else>{{ u.pct }}%</template>
            </span>
          </div>
          <div v-if="!uploads.length" class="empty">暂无上传任务</div>
        </div>

        <div class="upload-foot">
          <div class="upload-note">课件将按科目自动分类，保存到本机课件文件夹；无需登录，访问密钥由老师分享。</div>
          <button class="btn primary big" :disabled="!subject || !pendingUploads()" @click="startUpload">开始上传</button>
        </div>
      </section>

      <!-- ================= 总结文件（效果图 04 · 真实渲染） ================= -->
      <section v-if="tab === 'summaries'" class="page">
        <template v-if="currentSummary">
          <div class="sum-hero">
            <div>
              <h2 class="hero-title">课堂总结 · {{ currentSummary.name }}</h2>
              <div class="hero-sub">
                <span>{{ currentSummary.range }}</span>
                <span class="tag" :class="currentSummary.tagClass">{{ currentSummary.tag }}</span>
                <span class="hero-files">{{ currentSummary.fileCount }} 个文件</span>
              </div>
            </div>
            <span class="done-badge"><i class="dot"></i>已生成</span>
          </div>

          <div class="quality-row">
            <template v-if="qualityStars">
              <span class="q-label">课堂质量</span>
              <span class="stars">{{ qualityStars }}</span>
            </template>
            <span v-else class="q-label">课堂质量待评估</span>
            <a class="dl-link" :href="currentFileUrl" target="_blank" rel="noopener">下载原始 md</a>
          </div>

          <div class="three-col">
            <div class="card">
              <h3 class="card-title">老师总结</h3>
              <div v-for="g in adviceGroups" :key="g.label" class="advice-group">
                <div v-for="(a, i) in g.items" :key="i" class="advice-box">
                  <span class="adv-ico">
                    <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.9"
                         stroke-linecap="round" stroke-linejoin="round">
                      <path d="M4.5 19.5h15"/>
                      <path d="M6.8 16.2 4.6 19.4l3.2-.3 9.6-9.6-2.9-2.9z"/>
                      <path d="m15.6 4.4 1.3-1.3 3 3-1.3 1.3"/>
                    </svg>
                  </span>
                  <span class="adv-text"><b>{{ g.label }}：</b>{{ a }}</span>
                </div>
              </div>
              <div v-if="!adviceGroups.length" class="empty">本次总结未包含节奏建议（等待 AI 生成后刷新）</div>
            </div>

            <div class="card">
              <h3 class="card-title">学生总结</h3>
              <div class="kp-group">
                <div class="kp-label">本节重点</div>
                <ul class="kp-list" v-if="studentPoints.length">
                  <li v-for="(p, i) in studentPoints" :key="'p' + i">{{ p }}</li>
                </ul>
                <div v-else class="empty">本次总结未包含重点内容</div>
              </div>
              <div class="kp-group">
                <div class="kp-label">本节难点</div>
                <ul class="kp-list" v-if="studentDiffs.length">
                  <li v-for="(p, i) in studentDiffs" :key="'d' + i">{{ p }}</li>
                </ul>
                <div v-else class="empty">本次总结未包含难点内容</div>
              </div>
            </div>

            <div class="card">
              <h3 class="card-title">思维导图</h3>
              <div v-if="mindmapMd" class="mm-wrap">
                <svg v-show="mmOk" ref="mmSvg" class="mm-svg"></svg>
                <div v-if="!mmOk && !mmFallback" class="empty">正在渲染思维导图…</div>
                <div v-if="mmFallback" class="mm-fallback">
                  <div class="mm-tip">离线降级视图（Markmap 资源不可用）</div>
                  <div class="mm-tree">
                    <div v-for="(n, i) in mmTree" :key="i" class="mm-node" :class="'lv' + (n.level % 5)"
                         :style="{ marginLeft: (n.level * 16) + 'px' }">{{ n.text }}</div>
                  </div>
                </div>
              </div>
              <div v-else class="empty">本节课未生成思维导图</div>
            </div>
          </div>

          <!-- 分贝统计（面积图） -->
          <div class="card">
            <div class="card-head">
              <h3 class="card-title">课堂分贝统计（每分钟）</h3>
              <div class="db-legend">
                平均 <b>{{ avgDb }} dB</b> · 峰值 <b>{{ peakDb }} dB</b>
              </div>
            </div>
            <div class="db-chart">
              <svg :viewBox="`0 0 ${W} ${H}`" class="db-svg">
                <defs>
                  <linearGradient id="dbArea" x1="0" y1="0" x2="0" y2="1">
                    <stop offset="0%" stop-color="#14B8A6" stop-opacity=".32"/>
                    <stop offset="100%" stop-color="#14B8A6" stop-opacity=".03"/>
                  </linearGradient>
                </defs>
                <g v-for="g in gridY" :key="g.v">
                  <line :x1="padL" :y1="g.y" :x2="W - padR" :y2="g.y" class="grid-line"/>
                  <text :x="padL - 9" :y="g.y + 4" class="axis-txt" text-anchor="end">{{ g.v }}</text>
                </g>
                <polygon v-if="polyPoints"
                         :points="`${padL},${H - padB} ${polyPoints} ${W - padR},${H - padB}`"
                         fill="url(#dbArea)"/>
                <line v-if="dbSamples.length" :x1="padL" :y1="avgY" :x2="W - padR" :y2="avgY" class="avg-line"/>
                <polyline v-if="polyPoints" :points="polyPoints" fill="none" stroke="#0F766E"
                          stroke-width="2.2" stroke-linejoin="round" stroke-linecap="round"/>
                <circle v-for="(p, i) in dotPoints" :key="'d' + i" :cx="p.x" :cy="p.y" r="3"
                        fill="#fff" stroke="#14B8A6" stroke-width="2"/>
                <circle v-for="(p, i) in peakPoints" :key="'pk' + i" :cx="p.x" :cy="p.y" r="4.2"
                        fill="#F59E0B" stroke="#fff" stroke-width="1.6"/>
              </svg>
            </div>
            <div class="db-foot">
              X 轴：采样序号（0 → {{ dbSamples.length }}） · Y 轴：{{ dbRange.lo }}~{{ dbRange.hi }} dB ·
              <span v-if="!dbSamples.length">暂无分贝数据</span>
              <span v-else>橙色为峰值点，虚线为平均值</span>
            </div>
          </div>

          <!-- 原文渲染 -->
          <div class="card">
            <div class="card-head wrap">
              <h3 class="card-title">总结原文</h3>
              <div class="file-tabs">
                <button v-for="f in summaryFiles" :key="f.name" class="chip sm"
                        :class="{ on: activeFile && activeFile.name === f.name }" @click="openFile(f)">{{ f.name }}</button>
              </div>
            </div>
            <div v-if="mdLoading" class="empty">正在加载原文…</div>
            <div v-else-if="activeMd" class="md-body" v-html="mdHtml"></div>
            <div v-else class="empty">暂无可渲染的 Markdown 内容</div>
          </div>
        </template>
        <div v-else class="empty-hero">
          <span class="eh-ico">
            <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"
                 stroke-linecap="round" stroke-linejoin="round">
              <path d="M13.2 3.2H7.6A2.4 2.4 0 0 0 5.2 5.6v12.8a2.4 2.4 0 0 0 2.4 2.4h8.8a2.4 2.4 0 0 0 2.4-2.4V9z"/>
              <path d="M13.2 3.2V9h5.6"/>
            </svg>
          </span>
          <div class="eh-title">还没有打开任何总结</div>
          <div class="eh-sub">从「总览」页的课堂总结列表里点「查看」即可打开</div>
        </div>
      </section>

      <!-- ================= 教科书（需求 §8） ================= -->
      <section v-if="tab === 'textbook'" class="page">
        <!-- 首次引导 -->
        <div class="card" v-if="!tbStatus.grade">
          <div class="card-head">
            <h3 class="card-title">首次配置 · 选择年级与教材</h3>
            <span class="card-note">共 3 步</span>
          </div>

          <div class="step">
            <span class="step-no">1</span>
            <div class="step-body">
              <div class="step-title">选择年级</div>
              <div class="row">
                <select v-model="tbStage" class="input select" @change="onStageChange">
                  <option v-for="s in stages" :key="s" :value="s">{{ s }}</option>
                </select>
                <select v-model="tbGradeName" class="input select">
                  <option v-for="g in gradeOptions" :key="g" :value="g">{{ g }}</option>
                </select>
              </div>
            </div>
          </div>

          <div class="step">
            <span class="step-no">2</span>
            <div class="step-body">
              <div class="step-title">自动定位地区</div>
              <div class="row">
                <input v-model="tbRegion" class="input" placeholder="地区（留空点右侧按钮自动定位）" />
                <button class="btn" :disabled="tbMatching" @click="matchTextbooks">
                  {{ tbMatching ? '定位中…' : '自动定位并匹配教材' }}
                </button>
              </div>
              <div v-if="tbLocated" class="hint ok">已按公网 IP 定位到：{{ tbRegion || '默认地区' }}</div>
              <div v-else class="hint">未定位到地区时默认按人教版匹配；也可手动填写（如 广州、北京、上海、江苏、浙江、四川）</div>
            </div>
          </div>

          <div class="step" v-if="tbBookList.length">
            <span class="step-no">3</span>
            <div class="step-body">
              <div class="step-title">确认教材版本清单</div>
              <div class="book-list">
                <label v-for="b in tbBookList" :key="b.subject" class="book-row">
                  <input type="checkbox" v-model="tbBookSel[b.subject]" />
                  <span class="bk-subj">{{ b.subject }}</span>
                  <span class="bk-ver">{{ b.version }}</span>
                </label>
              </div>
              <div class="row">
                <button class="btn" @click="confirmBooks">确认清单</button>
                <span v-if="tbConfirmed" class="hint ok">已确认 {{ tbSelectedCount }} 本教材</span>
              </div>
            </div>
          </div>

          <div v-if="tbMsg" class="hint mt">{{ tbMsg }}</div>
          <div class="official-tip">
            <b>优先官方公开免费渠道：</b>国家中小学智慧教育平台 basic.smartedu.cn；
            无免费版时请导入 PDF 建立本地知识库（教材内容不上传，全部在本机检索）。
          </div>
        </div>

        <!-- 已配置 -->
        <div class="card" v-else>
          <div class="card-head">
            <h3 class="card-title">知识库状态</h3>
            <button class="btn ghost sm" @click="resetTextbookGuide">重新选择年级/地区</button>
          </div>
          <div class="tb-meta">
            <span>年级 <b>{{ tbStatus.grade }}</b></span>
            <span>地区 <b>{{ tbStatus.region || '默认（人教版）' }}</b></span>
            <span>片段总数 <b>{{ tbStatus.chunks || 0 }}</b></span>
            <span>向量模型 <b>{{ tbStatus.embed_model || '未配置' }}</b></span>
          </div>
          <div v-if="tbBookEntries.length" class="book-list">
            <div v-for="b in tbBookEntries" :key="b.name" class="book-row static">
              <span class="bk-subj">{{ b.name }}</span>
              <span class="bk-ver">{{ b.chapters }} 章 / {{ b.chunks }} 片段</span>
            </div>
          </div>
          <div v-else class="empty">尚未导入教材，请在下方导入 PDF / TXT / MD</div>
        </div>

        <!-- 导入 -->
        <div class="card mt">
          <h3 class="card-title">导入教材（PDF / TXT / MD）</h3>
          <div class="row">
            <input v-model="tbTitle" class="input" placeholder="教材名称（留空用文件名）" />
            <button class="btn" :disabled="tbImporting" @click="$refs.tbFile.click()">选择文件</button>
            <input type="file" ref="tbFile" hidden accept=".pdf,.txt,.md" @change="onTextbookFile" />
          </div>
          <div v-if="tbImportMsg" :class="['hint', tbImportOk ? 'ok' : 'warn']">{{ tbImportMsg }}</div>
          <div v-if="tbImportChunks" class="chunk-badge">已建库 {{ tbImportChunks }} 个片段</div>
        </div>

        <!-- 提问 -->
        <div class="card mt">
          <h3 class="card-title">教材问答</h3>
          <div class="row">
            <textarea v-model="tbQuestion" class="input area" rows="3"
                      placeholder="基于已入库教材提问，例如：二次函数的顶点公式是什么？"></textarea>
          </div>
          <div class="row">
            <button class="btn primary" :disabled="tbAsking || !tbQuestion.trim()" @click="askTextbook">
              {{ tbAsking ? '检索中…' : '提问' }}
            </button>
            <span class="hint">本地检索 + 本地模型生成，不上传教材内容</span>
          </div>
          <div v-if="tbAnswer" class="answer-box">
            <div class="md-body" v-html="renderMd(tbAnswer)"></div>
          </div>
          <div v-if="tbSources.length" class="src-list">
            <div class="src-head">引用来源</div>
            <div v-for="(s, i) in tbSources" :key="i" class="src-item">
              <span class="src-book">{{ s.book }}</span>
              <span class="src-chap">{{ s.chapter }}</span>
              <span class="src-score">相关度 {{ fmtScore(s.score) }}</span>
              <div class="src-text">{{ s.text }}</div>
            </div>
          </div>
        </div>
      </section>

      <!-- ================= 座位表（需求 §7） ================= -->
      <section v-if="tab === 'seat'" class="page">
        <div class="card">
          <div class="card-head">
            <h3 class="card-title">导入座位表</h3>
            <span class="card-note" v-if="seatRows">当前 {{ seatRows }} 排 × {{ seatCols }} 列</span>
          </div>
          <div class="row">
            <input v-model="className" class="input" placeholder="班级名称（可选）" />
            <button class="btn" :disabled="seatUploading" @click="$refs.seat.click()">
              {{ seatUploading ? '识别中…' : '上传座位表图片' }}
            </button>
            <input type="file" ref="seat" hidden accept="image/*" @change="onSeat" />
            <button class="btn ghost" :disabled="!seatCells.length" @click="exportSeat">导出座位表</button>
          </div>
          <div v-if="seatMsg" :class="['hint', seatOk ? 'ok' : 'warn']">{{ seatMsg }}</div>
        </div>

        <div class="card mt">
          <div class="card-head">
            <h3 class="card-title">座位图 · {{ seatMap.class_name || '未命名班级' }}</h3>
            <span class="card-note">讲台方向 ↑</span>
          </div>
          <div v-if="seatCells.length" class="seat-grid"
               :style="{ gridTemplateColumns: `repeat(${seatCols}, minmax(72px, 1fr))` }">
            <div v-for="(c, i) in seatCells" :key="i" class="seat" :class="{ vacant: !c.name }">
              <span class="seat-name">{{ c.name || '空位' }}</span>
              <span class="seat-idx">{{ c.row }} 排 {{ c.col }} 列</span>
            </div>
          </div>
          <div v-else class="empty-hero small">
            <span class="eh-ico">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.5"
                   stroke-linecap="round" stroke-linejoin="round">
                <rect x="3.6" y="4.6" width="16.8" height="12.8" rx="2.4"/>
                <path d="M8 20.4h8M12 17.4v3"/>
              </svg>
            </span>
            <div class="eh-title">还没有座位数据</div>
            <div class="eh-sub">请先上传座位表图片，由识图模型识别姓名与行列</div>
          </div>
        </div>

        <div class="card mt" v-if="seatUnresolved.length">
          <h3 class="card-title">待确认名单（{{ seatUnresolved.length }}）</h3>
          <div class="chips">
            <span v-for="(n, i) in seatUnresolved" :key="i" class="chip sm warn-chip">{{ n }}</span>
          </div>
        </div>
      </section>

      <!-- ================= 监控 ================= -->
      <section v-if="tab === 'monitor'" class="page">
        <div class="stat-grid">
          <div class="stat-card">
            <div class="stat-body">
              <div class="num" :class="{ sm: true }">{{ monitor ? '开启' : '关闭' }}</div>
              <div class="lbl">监控状态</div>
            </div>
            <span class="stat-ico" :class="{ warn: !monitor }">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"
                   stroke-linecap="round" stroke-linejoin="round">
                <path d="M2.8 12S6.4 5.6 12 5.6 21.2 12 21.2 12 17.6 18.4 12 18.4 2.8 12 2.8 12z"/>
                <circle cx="12" cy="12" r="3"/>
              </svg>
            </span>
          </div>
          <div class="stat-card">
            <div class="stat-body">
              <div class="num">{{ recordDays }}</div>
              <div class="lbl">录像天数</div>
            </div>
            <span class="stat-ico">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"
                   stroke-linecap="round" stroke-linejoin="round">
                <rect x="3.4" y="6.4" width="12.6" height="11.2" rx="2.6"/>
                <path d="m16 12 4.6-2.9v9.8L16 16z"/>
              </svg>
            </span>
          </div>
          <div class="stat-card">
            <div class="stat-body">
              <div class="num">14</div>
              <div class="lbl">保留策略（天）</div>
            </div>
            <span class="stat-ico">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"
                   stroke-linecap="round" stroke-linejoin="round">
                <circle cx="12" cy="12" r="8.4"/>
                <path d="M12 7.4V12l3.2 2"/>
              </svg>
            </span>
          </div>
          <div class="stat-card">
            <div class="stat-body">
              <div class="num sm">{{ tunnelOk ? '已连接' : '未连接' }}</div>
              <div class="lbl">隧道状态</div>
            </div>
            <span class="stat-ico" :class="{ warn: !tunnelOk }">
              <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"
                   stroke-linecap="round" stroke-linejoin="round">
                <path d="M4.6 10.4a7.4 7.4 0 0 1 10.4 0l-2 2a4.6 4.6 0 0 0-6.4 0z"/>
                <path d="M7.6 13.4a3.4 3.4 0 0 1 4.4 0"/>
                <path d="M12.4 12.4 20 20"/>
              </svg>
            </span>
          </div>
        </div>
        <div class="card">
          <h3 class="card-title">监控说明</h3>
          <p class="note">监控默认开启：按课程切片录制摄像头画面与麦克风录音，只保留最近 14 天；关机时自动导出归档；所有画面在本机处理，不上传原始画面。</p>
        </div>
      </section>

      <!-- ================= 设置 ================= -->
      <section v-if="tab === 'settings'" class="page">
        <div class="card">
          <div class="card-head">
            <h3 class="card-title">模型选择（Ollama）</h3>
            <span class="card-note">当前：{{ currentModel || '未选择' }}</span>
          </div>
          <div class="model-grid">
            <button v-for="m in modelList" :key="m.name" class="chip" :class="{ on: currentModel === m.name }"
                    @click="pickModel(m.name)">{{ m.name }}<small v-if="m.size"> {{ m.size }}</small></button>
            <span v-if="!modelList.length" class="empty">未检测到本地模型（请先执行 ollama pull）</span>
          </div>
          <div class="model-meta">识图模型 <b>{{ state.vision_model || '未配置' }}</b> · 公网 IP <b>{{ publicIp || '查询中…' }}</b></div>

          <h3 class="card-title mt">打包专属模型</h3>
          <div class="row">
            <input v-model="newModelName" class="input" placeholder="专属模型名（如 classroom-math）" />
            <select v-model="newModelBase" class="input select">
              <option value="">选择基础模型…</option>
              <option v-for="m in modelList" :key="m.name" :value="m.name">{{ m.name }}</option>
            </select>
            <button class="btn" :disabled="!newModelName.trim()" @click="createModel">创建</button>
          </div>
          <div v-if="createMsg" :class="['hint', createOk ? 'ok' : 'warn']">{{ createMsg }}</div>
        </div>

        <div class="card mt">
          <h3 class="card-title">技能库与硬件</h3>
          <div class="hw-grid">
            <div class="hw-item"><span class="hw-k">CPU</span><span class="hw-v">{{ hardware.cpu || '未知' }}</span></div>
            <div class="hw-item"><span class="hw-k">核心数</span><span class="hw-v">{{ hardware.cores || 0 }} 核</span></div>
            <div class="hw-item"><span class="hw-k">内存</span><span class="hw-v">{{ hardware.ram_gb || 0 }} GB</span></div>
            <div class="hw-item"><span class="hw-k">显卡</span><span class="hw-v">{{ hardware.gpu || '无' }}{{ hardware.vram_gb ? `（${hardware.vram_gb} GB）` : '' }}</span></div>
          </div>
          <div class="hint">{{ hardware.advice || '后端未返回硬件建议' }}</div>
        </div>

        <div class="card mt">
          <h3 class="card-title">隧道（Cloudflare 内网穿透）</h3>
          <div class="row">
            <input v-model="tokenInput" class="input" placeholder="粘贴 Cloudflare Tunnel Token" />
            <button class="btn" @click="saveToken">保存 Token</button>
            <button class="btn" :disabled="!tokenInput" @click="startTunnel">启动隧道</button>
            <button class="btn ghost" :disabled="tunnelLogining" @click="loginTunnel">
              {{ tunnelLogining ? '授权中…' : '登录授权' }}
            </button>
          </div>
          <div class="model-meta">当前状态 <b :class="tunnelOk ? 'ok' : 'bad'">{{ tunnelOk ? '已连接' : '未连接' }}</b></div>
          <div v-if="tunnelMsg" class="hint">{{ tunnelMsg }}</div>

          <h3 class="card-title mt">添加代理</h3>
          <div class="row">
            <input v-model="proxy.name" class="input" placeholder="服务名称（如 webui）" />
            <input v-model="proxy.port" class="input" placeholder="本机端口（如 18767）" />
            <input v-model="proxy.hostname" class="input" placeholder="域名（可选，留空自动生成）" />
            <button class="btn" @click="addProxy">添加</button>
          </div>

          <h3 class="card-title mt">代理规则</h3>
          <div v-if="ruleList.length" class="rule-list">
            <div v-for="(r, i) in ruleList" :key="i" class="rule-row">
              <span class="rule-name">{{ r.name }}</span>
              <span class="rule-val">{{ r.value }}</span>
            </div>
          </div>
          <div v-else class="empty">暂无代理规则</div>
        </div>

        <div class="card mt">
          <h3 class="card-title">工具入口</h3>
          <div class="tool-entry">
            <button class="te-btn" @click="tab = 'textbook'">
              <span class="te-ico">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"
                     stroke-linecap="round" stroke-linejoin="round">
                  <path d="M4 6.2A2.2 2.2 0 0 1 6.2 4H20v14H6.2A2.2 2.2 0 0 0 4 20.2z"/>
                  <path d="M4 18.2A2.2 2.2 0 0 1 6.2 16H20"/>
                </svg>
              </span>
              <span>教科书知识库</span>
              <small>年级地区匹配 · PDF 导入 · 向量化问答</small>
            </button>
            <button class="te-btn" @click="tab = 'seat'">
              <span class="te-ico">
                <svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8"
                     stroke-linecap="round" stroke-linejoin="round">
                  <circle cx="8" cy="8" r="2.4"/><circle cx="16" cy="8" r="2.4"/>
                  <circle cx="8" cy="16" r="2.4"/><circle cx="16" cy="16" r="2.4"/>
                </svg>
              </span>
              <span>座位表</span>
              <small>图片识别座位 · 换座检测 · 导出</small>
            </button>
          </div>
        </div>
      </section>
    </main>
  </div>
</template>
<script>
import api from './api.js'

/* ---------------- 轻量 Markdown → HTML（无第三方依赖） ---------------- */
function esc(s) {
  return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;')
}
function inlineMd(s) {
  let t = esc(s)
  t = t.replace(/`([^`]+)`/g, '<code>$1</code>')
  t = t.replace(/\*\*([^*]+)\*\*/g, '<strong>$1</strong>')
  t = t.replace(/(^|[^*])\*([^*\n]+)\*/g, '$1<em>$2</em>')
  t = t.replace(/~~([^~]+)~~/g, '<del>$1</del>')
  t = t.replace(/\[([^\]]+)\]\((https?:\/\/[^)\s]+)\)/g,
    '<a href="$2" target="_blank" rel="noopener">$1</a>')
  return t
}
function renderMarkdown(md) {
  if (!md) return ''
  const lines = String(md).replace(/\r\n/g, '\n').split('\n')
  const out = []
  let i = 0
  const isBreak = (l) => !l.trim() ||
    /^#{1,6}\s/.test(l) || /^\s*>\s?/.test(l) || /^\s*```/.test(l) ||
    /^\s*[-*+]\s+/.test(l) || /^\s*\d+[.、)]\s+/.test(l) ||
    /^\s*(-{3,}|\*{3,}|_{3,})\s*$/.test(l) || /^\s*\|.*\|\s*$/.test(l)
  while (i < lines.length) {
    const line = lines[i]
    // 代码块
    if (/^\s*```/.test(line)) {
      const buf = []
      i++
      while (i < lines.length && !/^\s*```/.test(lines[i])) { buf.push(lines[i]); i++ }
      i++
      out.push(`<pre><code>${esc(buf.join('\n'))}</code></pre>`)
      continue
    }
    // 标题
    const h = /^(#{1,6})\s+(.*)$/.exec(line)
    if (h) {
      const lv = Math.min(h[1].length, 4)
      out.push(`<h${lv}>${inlineMd(h[2])}</h${lv}>`)
      i++
      continue
    }
    // 分隔线
    if (/^\s*(-{3,}|\*{3,}|_{3,})\s*$/.test(line)) { out.push('<hr/>'); i++; continue }
    // 引用
    if (/^\s*>\s?/.test(line)) {
      const buf = []
      while (i < lines.length && /^\s*>\s?/.test(lines[i])) { buf.push(lines[i].replace(/^\s*>\s?/, '')); i++ }
      out.push(`<blockquote>${renderMarkdown(buf.join('\n'))}</blockquote>`)
      continue
    }
    // 表格
    if (/^\s*\|.*\|\s*$/.test(line) && i + 1 < lines.length && /^\s*\|[\s:|-]+\|\s*$/.test(lines[i + 1])) {
      const row = (r) => r.trim().replace(/^\|/, '').replace(/\|$/, '').split('|').map((c) => c.trim())
      const head = row(line)
      i += 2
      const body = []
      while (i < lines.length && /^\s*\|.*\|\s*$/.test(lines[i])) { body.push(row(lines[i])); i++ }
      out.push(
        `<table><thead><tr>${head.map((c) => `<th>${inlineMd(c)}</th>`).join('')}</tr></thead>` +
        `<tbody>${body.map((r) => `<tr>${r.map((c) => `<td>${inlineMd(c)}</td>`).join('')}</tr>`).join('')}</tbody></table>`
      )
      continue
    }
    // 列表
    const ul = /^\s*[-*+]\s+/
    const ol = /^\s*\d+[.、)]\s+/
    if (ul.test(line) || ol.test(line)) {
      const re = ul.test(line) ? ul : ol
      const tag = ul.test(line) ? 'ul' : 'ol'
      const items = []
      while (i < lines.length && re.test(lines[i])) {
        let t = lines[i].replace(re, '')
        i++
        while (i < lines.length && /^\s{2,}\S/.test(lines[i]) && !re.test(lines[i]) && !/^\s*[-*+]\s+/.test(lines[i])) {
          t += ' ' + lines[i].trim()
          i++
        }
        items.push(`<li>${inlineMd(t)}</li>`)
      }
      out.push(`<${tag}>${items.join('')}</${tag}>`)
      continue
    }
    if (!line.trim()) { i++; continue }
    // 段落
    const buf = []
    while (i < lines.length && !isBreak(lines[i])) { buf.push(lines[i]); i++ }
    if (buf.length) out.push(`<p>${inlineMd(buf.join('\n')).replace(/\n/g, '<br/>')}</p>`)
    else i++
  }
  return out.join('')
}
/* 取 md 中某个小节下的列表项（用于解析老师/学生总结） */
function mdSection(md, keywords) {
  const lines = String(md || '').split('\n')
  const out = []
  let on = false
  let base = 6
  for (const raw of lines) {
    const line = raw.trim()
    const h = /^(#{1,6})\s+(.*)$/.exec(line)
    if (h) {
      if (keywords.some((k) => h[2].includes(k))) { on = true; base = h[1].length; continue }
      if (on && h[1].length <= base) { on = false }
      continue
    }
    if (!on || !line) continue
    const item = line.replace(/^[-*+]\s+/, '').replace(/^\d+[.、)]\s+/, '').trim()
    if (item && !/^（待生成）$/.test(item) && item !== '待生成') out.push(item)
  }
  return out
}

const DEFAULT_SUBJECTS = ['语文', '数学', '英语', '物理', '化学', '生物', '历史', '地理', '政治']

export default {
  data() {
    return {
      tab: 'overview',
      tabs: [
        { key: 'overview', label: '总览' },
        { key: 'courseware', label: '课程' },
        { key: 'summaries', label: '总结文件' },
        { key: 'monitor', label: '监控' },
        { key: 'settings', label: '设置' },
      ],
      state: {},
      dbData: {},
      summaries: [],
      schedule: {},
      uploads: [],
      subject: '',
      subjects: [],
      courseware: {},
      dragOver: false,
      pickedFiles: [],
      tokenInput: '',
      className: '',
      publicIp: '',
      modelList: [],
      currentModel: '',
      hardware: {},
      newModelName: '',
      newModelBase: '',
      createMsg: '',
      createOk: false,
      currentSummary: null,
      summaryFiles: [],
      activeFile: null,
      activeMd: '',
      mdLoading: false,
      teacherMd: '',
      studentMd: '',
      mindmapMd: '',
      mmOk: false,
      mmFallback: false,
      mmInstance: null,
      // 教科书
      stages: ['小学', '初中', '高中'],
      gradeNames: {
        小学: ['一年级', '二年级', '三年级', '四年级', '五年级', '六年级'],
        初中: ['七年级', '八年级', '九年级'],
        高中: ['高一', '高二', '高三'],
      },
      tbStatus: {},
      tbStage: '小学',
      tbGradeName: '三年级',
      tbRegion: '',
      tbBooks: {},
      tbBookSel: {},
      tbMsg: '',
      tbLocated: false,
      tbMatching: false,
      tbConfirmed: false,
      tbTitle: '',
      tbImporting: false,
      tbImportMsg: '',
      tbImportOk: false,
      tbImportChunks: 0,
      tbQuestion: '',
      tbAnswer: '',
      tbSources: [],
      tbAsking: false,
      // 座位表
      seatMap: {},
      seatMsg: '',
      seatOk: false,
      seatUploading: false,
      // 隧道
      tunnelRules: {},
      tunnelMsg: '',
      tunnelLogining: false,
      proxy: { name: '', port: '', hostname: '' },
      // 图表
      W: 600, H: 220, padL: 34, padR: 10, padT: 14, padB: 28,
    }
  },
  computed: {
    minutesLeft() {
      const s = this.state.session
      if (!s) return 0
      const dur = s.duration_min || 40
      const el = s.elapsed_min || 0
      return Math.max(0, Math.round(dur - el))
    },
    monitor() { return !!this.state.monitor_enabled },
    pageTitle() {
      const map = {
        overview: '数据中心',
        courseware: '课件上传',
        summaries: this.currentSummary ? '课堂总结' : '总结文件',
        monitor: '监控',
        settings: '设置',
        textbook: '教科书',
        seat: '座位表',
      }
      return map[this.tab] || '数据中心'
    },
    pageStatus() {
      if (this.tab === 'summaries' && (this.currentSummary || this.summaryCards.length)) return '已生成'
      if (this.tab === 'courseware' && this.pendingUploads()) return '上传中'
      return ''
    },
    tunnelOk() { return !!(this.state.tunnel && this.state.tunnel.running) },
    avgDb() { return this.dbData.avg_db || 0 },
    peakDb() { return this.dbData.peak_db || 0 },
    dbSamples() {
      const s = this.dbData.samples
      return Array.isArray(s) ? s.filter((v) => typeof v === 'number' && isFinite(v)) : []
    },
    todayCount() { return this.scheduleItems.length || this.summaryCards.length },
    overtimeCount() { return this.state.session && this.state.session.overtime ? 1 : 0 },
    exportCount() { return this.summaries.reduce((n, d) => n + d.courses.reduce((m, c) => m + c.files.length, 0), 0) },
    exportDays() {
      return this.summaries.map(d => ({ date: d.date, count: d.courses.reduce((m, c) => m + c.files.length, 0) }))
    },
    recordDays() { return this.summaries.length },
    todayStr() {
      const d = new Date(); const p = n => String(n).padStart(2, '0')
      return `${d.getFullYear()}-${p(d.getMonth() + 1)}-${p(d.getDate())}`
    },
    scheduleItems() {
      const items = (this.schedule && this.schedule.items) || []
      return items.filter(it => it && (it.subject || it.start))
    },
    summaryCards() {
      const list = []
      for (const d of this.summaries) {
        for (const c of d.courses) {
          const mds = c.files.filter(f => f.name.endsWith('.md'))
          if (!mds.length) continue
          const tag = c.name.includes('评讲') ? '评讲课'
            : (c.name.includes('考试') || c.name.includes('自习') ? '考试/自习' : '新授课')
          const tagClass = tag === '新授课' ? 'new' : (tag === '评讲课' ? 'review' : 'exam')
          list.push({
            name: c.name, tag, tagClass, range: d.date, date: d.date, course: c.name,
            files: mds, fileCount: c.files.length,
          })
        }
      }
      return list.slice(0, 6)
    },
    currentFileUrl() {
      if (!this.activeFile) return ''
      const s = this.currentSummary
      return `/api/files/${encodeURIComponent(s ? s.date : '')}/${encodeURIComponent(s ? s.course : '')}/${encodeURIComponent(this.activeFile.name)}`
    },
    mdHtml() { return renderMarkdown(this.activeMd) },
    qualityStars() {
      const m = String(this.teacherMd || '').match(/[★☆]{1,6}/)
      return m ? m[0] : ''
    },
    adviceGroups() {
      const defs = [
        { label: '建议细讲', keys: ['建议细讲', '细讲'] },
        { label: '可缩短', keys: ['建议缩短', '可缩短', '缩短'] },
        { label: '防拖堂建议', keys: ['防拖堂'] },
      ]
      const src = this.teacherMd || this.activeMd || ''
      return defs
        .map(d => ({ label: d.label, items: mdSection(src, d.keys) }))
        .filter(g => g.items.length)
    },
    studentPoints() { return mdSection(this.studentMd, ['本节重点', '重点']) },
    studentDiffs() { return mdSection(this.studentMd, ['本节难点', '难点']) },
    mmTree() {
      const out = []
      for (const raw of String(this.mindmapMd || '').split('\n')) {
        if (!raw.trim()) continue
        const h = /^(#{1,6})\s+(.*)$/.exec(raw.trim())
        if (h) {
          out.push({ level: Math.max(0, h[1].length - 1), text: h[2].replace(/\*\*/g, '') })
          continue
        }
        const m = /^(\s*)[-*+]\s+(.*)$/.exec(raw)
        if (m) {
          out.push({ level: Math.min(6, Math.floor(m[1].length / 2) + 1), text: m[2].replace(/\*\*/g, '') })
          continue
        }
        out.push({ level: Math.min(6, Math.floor((raw.match(/^\s*/) || [''])[0].length / 2)), text: raw.trim() })
      }
      return out.slice(0, 200)
    },
    // ---- 分贝图：真实 dB 刻度（默认 30~90 自适应）----
    dbRange() {
      const s = this.dbSamples
      let lo = 30, hi = 90
      if (s.length) {
        const mn = Math.min(...s), mx = Math.max(...s)
        lo = Math.min(30, Math.floor(mn / 5) * 5)
        hi = Math.max(90, Math.ceil(mx / 5) * 5)
        if (mx < 70) hi = Math.max(60, Math.ceil(mx / 5) * 5 + 5)
        if (mn > 50) lo = Math.max(20, Math.floor(mn / 5) * 5 - 5)
      }
      if (hi - lo < 10) hi = lo + 10
      return { lo, hi }
    },
    plotBox() {
      return {
        w: this.W - this.padL - this.padR,
        h: this.H - this.padT - this.padB,
      }
    },
    gridY() {
      const { lo, hi } = this.dbRange
      const out = []
      for (let k = 0; k <= 4; k++) {
        const v = hi - (hi - lo) * (k / 4)
        out.push({ v: Math.round(v), y: this.dbY(v) })
      }
      return out
    },
    polyPoints() {
      const s = this.dbSamples
      if (s.length < 2) return ''
      return s.map((v, i) => `${this.dbX(i, s.length).toFixed(1)},${this.dbY(v).toFixed(1)}`).join(' ')
    },
    peakPoints() {
      const s = this.dbSamples
      if (!s.length) return []
      const mx = Math.max(...s)
      if (!(mx > 0)) return []
      const out = []
      s.forEach((v, i) => { if (v === mx) out.push({ x: this.dbX(i, s.length), y: this.dbY(v) }) })
      return out.slice(0, 12)
    },
    avgY() { return this.dbY(this.avgDb) },
    // 面积图上的数据点（均匀取样，最多约 10 个，避免过密）
    dotPoints() {
      const s = this.dbSamples
      if (!s.length) return []
      const step = Math.max(1, Math.ceil(s.length / 10))
      const out = []
      for (let i = 0; i < s.length; i += step) {
        out.push({ x: this.dbX(i, s.length), y: this.dbY(s[i]) })
      }
      return out
    },
    // ---- 教科书 ----
    gradeOptions() { return this.gradeNames[this.tbStage] || [] },
    tbBookList() {
      return Object.keys(this.tbBooks || {}).map(k => ({ subject: k, version: this.tbBooks[k] }))
    },
    tbSelectedCount() {
      return Object.keys(this.tbBookSel || {}).filter(k => this.tbBookSel[k]).length
    },
    tbBookEntries() {
      const books = (this.tbStatus && this.tbStatus.books) || {}
      return Object.keys(books).map(name => ({
        name,
        chapters: (books[name] && books[name].chapters) || 0,
        chunks: (books[name] && books[name].chunks) || 0,
      }))
    },
    // ---- 座位表 ----
    seatRows() {
      const d = this.seatMap || {}
      if (d.rows) return d.rows
      const seats = d.seats || []
      return seats.length ? Math.max(...seats.map(s => s.row || 0)) : 0
    },
    seatCols() {
      const d = this.seatMap || {}
      if (d.cols) return d.cols
      const seats = d.seats || []
      return seats.length ? Math.max(...seats.map(s => s.col || 0)) : 0
    },
    seatCells() {
      const d = this.seatMap || {}
      const rows = this.seatRows, cols = this.seatCols
      if (!rows || !cols) return []
      const map = {}
      for (const s of (d.seats || [])) map[`${s.row}-${s.col}`] = s.name || ''
      const cells = []
      for (let r = 1; r <= rows; r++) {
        for (let c = 1; c <= cols; c++) cells.push({ row: r, col: c, name: map[`${r}-${c}`] || '' })
      }
      return cells
    },
    seatUnresolved() { return (this.seatMap && this.seatMap.unresolved) || [] },
    ruleList() {
      const rules = (this.tunnelRules && this.tunnelRules.rules) || {}
      return Object.keys(rules).map(k => ({ name: k, value: rules[k] }))
    },
  },
  async mounted() {
    this.poll()
    setInterval(() => this.poll(), 10000)
    this.loadModels()
    this.loadCourseware()
    this.loadTextbookStatus()
    this.loadSeatmap()
    this.loadTunnelRules()
    // 演示模式：注入上传列表样例（用于视觉核对，不影响正常逻辑）
    if (/[?&]demo=1/.test(location.search)) {
      this.subject = '数学'
      this.uploads = [
        { name: '数学-二次函数.pptx', done: false, pct: 78 },
        { name: '语文-文言文阅读.pdf', done: true, pct: 100 },
      ]
    }
  },
  methods: {
    // ---------- 轮询 ----------
    async poll() {
      try { this.state = await api.state() } catch (e) { /* 后端不可用：保留上次数据 */ }
      try { this.dbData = await api.db() } catch (e) {}
      try { this.summaries = await api.summaries() } catch (e) {}
      try { this.publicIp = (await api.publicIp()).ip || '' } catch (e) {}
      try { this.schedule = await api.schedule() } catch (e) {}
    },
    async loadModels() {
      try {
        const m = await api.models()
        this.modelList = m.models || []
        this.currentModel = m.current || ''
        this.hardware = m.hardware || {}
      } catch (e) {}
    },
    async loadCourseware() {
      try {
        const c = await api.courseware() || {}
        this.courseware = c
        const keys = Object.keys(c)
        // 后端（config 已登记科目 + 课件目录）为准，接口为空时兜底常用科目
        const base = keys.length ? keys : DEFAULT_SUBJECTS
        const merged = []
        for (const s of [...base, ...this.subjects, this.subject]) {
          if (s && !merged.includes(s)) merged.push(s)
        }
        this.subjects = merged
        if (!this.subject || !this.subjects.includes(this.subject)) this.subject = this.subjects[0] || ''
      } catch (e) {
        this.subjects = DEFAULT_SUBJECTS.slice()
        this.subject = this.subject || '数学'
      }
    },
    // ---------- 分贝图坐标 ----------
    dbX(i, n) {
      const { w } = this.plotBox
      return this.padL + (n <= 1 ? 0 : (i * w) / (n - 1))
    },
    dbY(v) {
      const { lo, hi } = this.dbRange
      const { h } = this.plotBox
      const t = (v - lo) / (hi - lo)
      return this.padT + (1 - Math.min(Math.max(t, 0), 1)) * h
    },
    // ---------- 总结渲染 ----------
    isTeacherFile(f) { return /老师课堂总结|^01-/.test(f.name) },
    isStudentFile(f) { return /学生课堂总结|^02-/.test(f.name) },
    isMindmapFile(f) { return /思维导图|^05-/.test(f.name) },
    async openSummary(s) {
      this.tab = 'summaries'
      this.currentSummary = s
      this.summaryFiles = s.files || []
      this.activeFile = null
      this.activeMd = ''
      this.teacherMd = ''
      this.studentMd = ''
      this.mindmapMd = ''
      this.mmOk = false
      this.mmFallback = false
      const files = this.summaryFiles
      const t = files.find(this.isTeacherFile) || files[0]
      const st = files.find(this.isStudentFile)
      const mm = files.find(this.isMindmapFile)
      const get = async (f) => {
        if (!f) return ''
        try { return (await api.fileContent(s.date, s.course, f.name)).content || '' } catch (e) { return '' }
      }
      const [tm, sm, mmd] = await Promise.all([get(t), get(st), get(mm)])
      this.teacherMd = tm
      this.studentMd = sm
      this.mindmapMd = mmd
      if (t) this.openFile(t)
      if (mmd) this.renderMarkmap()
    },
    async openFile(f) {
      const s = this.currentSummary
      this.activeFile = f
      this.mdLoading = true
      try {
        const r = await api.fileContent(s.date, s.course, f.name)
        this.activeMd = r.content || ''
      } catch (e) {
        this.activeMd = ''
      } finally {
        this.mdLoading = false
      }
    },
    async renderMarkmap() {
      this.mmOk = false
      this.mmFallback = false
      await this.$nextTick()
      const el = this.$refs.mmSvg
      if (!el) return
      // CDN 不可达时 import() 会长挂起而不是立刻失败，这里加超时确保能降级
      const timeout = new Promise((_, rej) => setTimeout(() => rej(new Error('markmap cdn timeout')), 4000))
      try {
        // 动态从 CDN 加载，失败/超时则降级为缩进树
        const libUrl = 'https://esm.sh/markmap-lib'
        const viewUrl = 'https://esm.sh/markmap-view'
        const [lib, view] = await Promise.race([
          Promise.all([
            import(/* @vite-ignore */ libUrl),
            import(/* @vite-ignore */ viewUrl),
          ]),
          timeout,
        ])
        const { Transformer } = lib
        const { Markmap } = view
        const { root } = new Transformer().transform(this.mindmapMd)
        el.innerHTML = ''
        this.mmInstance = Markmap.create(el, { autoFit: true, duration: 300, maxWidth: 240 }, root)
        // 数据渲染完成后主动 fit 一次，避免图形偏小/偏移
        setTimeout(() => {
          try { this.mmInstance && this.mmInstance.fit() } catch (err) { /* 忽略 */ }
        }, 420)
        this.mmOk = true
      } catch (e) {
        this.mmFallback = true
      }
    },
    // ---------- 科目 / 上传 ----------
    pendingUploads() { return this.uploads.filter(u => !u.done).length },
    newSubject() {
      const name = prompt('输入新科目名称')
      if (name && !this.subjects.includes(name)) { this.subjects.push(name); this.subject = name }
    },
    onDrop(e) {
      this.dragOver = false
      this.pickedFiles = [...(e.dataTransfer?.files || [])]
      this.stageFiles()
    },
    onPick(e) {
      this.pickedFiles = [...(e.target.files || [])]
      this.stageFiles()
    },
    stageFiles() {
      for (const f of this.pickedFiles) this.uploads.push({ name: f.name, done: false, pct: 0, file: f })
      this.pickedFiles = []
    },
    async startUpload() {
      for (const u of this.uploads.filter(u => !u.done)) {
        try {
          u.pct = 100
          await api.upload(u.file, this.subject)
          u.done = true
        } catch (e) {
          u.pct = 0
        }
      }
      this.loadCourseware()
    },
    openExport(d) {
      const day = this.summaries.find(x => x.date === d.date)
      const course = day && day.courses.find(c => c.files.some(f => f.name.endsWith('.md')))
      if (!course) return
      const tag = course.name.includes('评讲') ? '评讲课'
        : (course.name.includes('考试') || course.name.includes('自习') ? '考试/自习' : '新授课')
      const tagClass = tag === '新授课' ? 'new' : (tag === '评讲课' ? 'review' : 'exam')
      this.openSummary({
        name: course.name, tag, tagClass, range: d.date, date: d.date, course: course.name,
        files: course.files.filter(f => f.name.endsWith('.md')), fileCount: course.files.length,
      })
    },
    // ---------- 模型 ----------
    pickModel(m) {
      this.currentModel = m
      api.selectModel(m).then(() => this.poll()).catch(() => {})
    },
    async createModel() {
      this.createMsg = '正在创建…'
      try {
        const r = await api.createModel(this.newModelName.trim(), this.newModelBase)
        this.createOk = !!r.ok
        this.createMsg = r.msg || (r.ok ? '创建成功' : '创建失败')
        if (r.ok) this.loadModels()
      } catch (e) {
        this.createOk = false
        this.createMsg = '创建失败：后端未响应'
      }
    },
    // ---------- 隧道 ----------
    async saveToken() {
      if (!this.tokenInput) return
      try { await api.setToken(this.tokenInput) } catch (e) {}
      this.loadTunnelRules()
      this.poll()
    },
    async startTunnel() {
      try { await api.startTunnel() } catch (e) {}
      this.loadTunnelRules()
      this.poll()
    },
    async loginTunnel() {
      this.tunnelLogining = true
      this.tunnelMsg = '正在唤起 cloudflared 登录授权…'
      try {
        const r = await api.tunnelLogin()
        this.tunnelMsg = r.msg || (r.ok ? '登录授权完成' : '登录未完成')
      } catch (e) {
        this.tunnelMsg = '登录失败：后端未响应'
      } finally {
        this.tunnelLogining = false
        this.loadTunnelRules()
      }
    },
    async addProxy() {
      const port = parseInt(this.proxy.port, 10)
      if (!this.proxy.name || !port) { this.tunnelMsg = '请填写服务名称与本机端口'; return }
      try {
        const r = await api.addProxy(this.proxy.name, port, this.proxy.hostname)
        this.tunnelMsg = r.msg || (r.ok ? `已添加代理 ${this.proxy.name} → ${port}` : '添加失败')
        if (r.ok) { this.proxy = { name: '', port: '', hostname: '' } }
      } catch (e) {
        this.tunnelMsg = '添加失败：后端未响应'
      }
      this.loadTunnelRules()
    },
    async loadTunnelRules() {
      try { this.tunnelRules = await api.tunnelRules() } catch (e) {}
    },
    // ---------- 教科书 ----------
    async loadTextbookStatus() {
      try {
        this.tbStatus = await api.textbookStatus()
        if (this.tbStatus.region) this.tbRegion = this.tbStatus.region
      } catch (e) {}
    },
    onStageChange() {
      this.tbGradeName = this.gradeOptions[0] || ''
    },
    async matchTextbooks() {
      this.tbMatching = true
      this.tbMsg = ''
      try {
        const grade = `${this.tbStage}${this.tbGradeName}`
        const r = await api.textbookMatch(grade, this.tbRegion)
        this.tbRegion = r.region || this.tbRegion
        this.tbLocated = !!r.located
        this.tbBooks = r.books || {}
        this.tbBookSel = {}
        for (const k of Object.keys(this.tbBooks)) this.tbBookSel[k] = true
        this.tbMsg = r.msg || ''
        this.loadTextbookStatus()
      } catch (e) {
        this.tbMsg = '教材匹配失败：后端未响应'
      } finally {
        this.tbMatching = false
      }
    },
    confirmBooks() {
      this.tbConfirmed = true
      this.loadTextbookStatus()
    },
    resetTextbookGuide() {
      this.tbStatus = { ...this.tbStatus, grade: '' }
      this.tbConfirmed = false
      this.tbBooks = {}
      this.tbBookSel = {}
    },
    async onTextbookFile(e) {
      const f = e.target.files[0]
      if (!f) return
      this.tbImporting = true
      this.tbImportMsg = `正在解析《${this.tbTitle || f.name}》并建库…`
      this.tbImportChunks = 0
      try {
        const r = await api.textbookImport(f, this.tbTitle)
        this.tbImportOk = !!r.ok
        this.tbImportMsg = r.msg || (r.ok ? '导入成功' : '导入失败')
        this.tbImportChunks = r.chunks || 0
        this.loadTextbookStatus()
      } catch (err) {
        this.tbImportOk = false
        this.tbImportMsg = '导入失败：后端未响应'
      } finally {
        this.tbImporting = false
        e.target.value = ''
      }
    },
    async askTextbook() {
      const q = this.tbQuestion.trim()
      if (!q) return
      this.tbAsking = true
      this.tbAnswer = ''
      this.tbSources = []
      try {
        const r = await api.textbookAsk(q, 5)
        this.tbAnswer = r.answer || '（未返回答案）'
        this.tbSources = r.sources || []
      } catch (e) {
        this.tbAnswer = '提问失败：后端未响应'
      } finally {
        this.tbAsking = false
      }
    },
    fmtScore(s) {
      const n = Number(s)
      return isFinite(n) ? n.toFixed(2) : '-'
    },
    renderMd(md) { return renderMarkdown(md) },
    // ---------- 座位表 ----------
    async loadSeatmap() {
      try {
        const r = await api.seatmap()
        this.seatMap = r.data || {}
      } catch (e) {}
    },
    async onSeat(e) {
      const f = e.target.files[0]
      if (!f) return
      this.seatUploading = true
      this.seatMsg = '正在识别座位表…'
      try {
        const r = await api.initSeatmap(f, this.className)
        this.seatOk = !!r.ok
        this.seatMsg = r.ok
          ? `识别成功：${r.seats || 0} 个座位${r.unresolved && r.unresolved.length ? `，${r.unresolved.length} 个待确认` : ''}`
          : (r.msg || '识别失败')
        this.loadSeatmap()
      } catch (err) {
        this.seatOk = false
        this.seatMsg = '座位表导入失败：后端未响应'
      } finally {
        this.seatUploading = false
        e.target.value = ''
      }
    },
    async exportSeat() {
      try {
        const r = await api.exportSeatmap()
        const md = r.markdown || ''
        const blob = new Blob([md], { type: 'text/markdown;charset=utf-8' })
        const url = URL.createObjectURL(blob)
        const a = document.createElement('a')
        a.href = url
        a.download = `座位表-${this.seatMap.class_name || '班级'}.md`
        document.body.appendChild(a)
        a.click()
        document.body.removeChild(a)
        URL.revokeObjectURL(url)
      } catch (e) {
        this.seatMsg = '导出失败：后端未响应'
      }
    },
  },
}
</script>
<style>
/* ==========================================================================
   AI 课堂助手 · 视觉规范（对照 docs/ui-design 效果图 01-04）
   深青主色 #0F766E · 强调青 #14B8A6 · 浅灰背景 · 白底圆角卡片
   ========================================================================== */
:root {
  --primary: #0F766E;
  --primary-deep: #0B5F59;
  --header-bg: #0A4E48;
  --accent: #14B8A6;
  --soft: #E3F6F3;
  --soft-line: #A7DED6;
  --bg: #EFF3F7;
  --card: #FFFFFF;
  --border: #E6ECF2;
  --border-strong: #D8E1EA;
  --ink: #0F172A;
  --text: #334155;
  --sub: #64748B;
  --muted: #94A3B8;
  --ok: #16A34A;
  --warn: #D97706;
  --danger: #DC2626;
  --shadow: 0 1px 2px rgba(15,23,42,.04), 0 6px 18px rgba(15,23,42,.04);
}
* { margin: 0; padding: 0; box-sizing: border-box; }
body {
  background: var(--bg);
  font-family: "Microsoft YaHei UI", "PingFang SC", "Hiragino Sans GB", system-ui, -apple-system, sans-serif;
  color: var(--ink);
  -webkit-font-smoothing: antialiased;
}
.app { min-height: 100vh; display: flex; flex-direction: column; }

/* ---------------- 顶部导航：深青实色条 ---------------- */
.topbar {
  display: flex; align-items: center; gap: 24px;
  background: var(--header-bg);
  color: #fff; padding: 0 28px; height: 58px; flex: none;
  position: sticky; top: 0; z-index: 20;
  box-shadow: 0 1px 0 rgba(0,0,0,.06);
}
.brand { display: flex; align-items: center; gap: 9px; flex: none; }
.brand-badge {
  width: 30px; height: 30px; border-radius: 8px; flex: none;
  background: rgba(255,255,255,.16); color: #fff;
  display: flex; align-items: center; justify-content: center;
}
.brand-name { font-size: 16px; font-weight: 700; letter-spacing: .3px; }
.brand-sep { color: rgba(255,255,255,.42); font-size: 15px; }
.brand-page { font-size: 15px; font-weight: 400; color: rgba(255,255,255,.88); }

.nav {
  display: flex; gap: 2px; flex: 1; margin-left: 20px;
  overflow-x: auto; scrollbar-width: none; justify-content: flex-end;
}
.nav::-webkit-scrollbar { display: none; }
.nav-btn {
  position: relative; background: none; border: none; cursor: pointer;
  color: rgba(255,255,255,.76); font-size: 14px; font-family: inherit;
  padding: 18px 16px; transition: color .15s; white-space: nowrap;
}
.nav-btn:hover { color: #fff; }
.nav-btn.active { color: #fff; font-weight: 600; }
.nav-btn.active::after {
  content: ''; position: absolute; left: 14px; right: 14px; bottom: 0;
  height: 2.5px; border-radius: 2px; background: #fff;
}
.top-status {
  flex: none; display: flex; align-items: center; gap: 7px;
  font-size: 13px; font-weight: 600; color: #D7FBF1;
}
.top-status .dot {
  width: 8px; height: 8px; border-radius: 50%; background: #5EEAD4;
  box-shadow: 0 0 0 3px rgba(94,234,212,.22);
}

/* ---------------- 内容容器 ---------------- */
.content { flex: 1; width: 100%; max-width: 1240px; margin: 0 auto; padding: 22px 28px 52px; }
.page { display: flex; flex-direction: column; gap: 18px; }

/* ---------------- 卡片 ---------------- */
.card {
  background: var(--card); border: 1px solid var(--border);
  border-radius: 14px; padding: 18px 20px; box-shadow: var(--shadow);
}
.card + .card { margin-top: 0; }
.card.mt { margin-top: 0; }
.card-head { display: flex; align-items: center; justify-content: space-between; gap: 12px; margin-bottom: 12px; }
.card-head.wrap { flex-wrap: wrap; }
.card-title { font-size: 15px; font-weight: 700; color: var(--ink); }
.card-title.mt { margin-top: 20px; margin-bottom: 12px; }
.card-note { font-size: 12.5px; font-weight: 400; color: var(--muted); }
.hint { color: var(--sub); font-size: 13px; line-height: 1.7; }
.hint.mt { margin-top: 12px; }
.hint.ok { color: var(--ok); font-weight: 600; }
.hint.warn { color: var(--warn); font-weight: 600; }
.note { color: var(--text); font-size: 13.5px; line-height: 1.85; }
.empty { color: var(--muted); font-size: 13px; padding: 14px 0; }

/* 空态 */
.empty-hero { text-align: center; padding: 64px 20px; }
.empty-hero.small { padding: 34px 20px; }
.eh-ico {
  display: inline-flex; width: 56px; height: 56px; border-radius: 16px;
  background: var(--soft); color: var(--accent);
  align-items: center; justify-content: center; margin-bottom: 14px;
}
.eh-ico svg { width: 28px; height: 28px; }
.eh-title { font-size: 16px; font-weight: 700; color: var(--ink); }
.eh-sub { font-size: 13px; color: var(--sub); margin-top: 6px; }

/* ---------------- 统计卡（大数字 + 浅青图标块） ---------------- */
.stat-grid { display: grid; grid-template-columns: repeat(4, 1fr); gap: 16px; }
.stat-card {
  background: var(--card); border: 1px solid var(--border); border-radius: 14px;
  padding: 20px 20px; box-shadow: var(--shadow);
  display: flex; align-items: center; justify-content: space-between; gap: 12px;
}
.stat-body { min-width: 0; }
.stat-card .num {
  font-size: 34px; font-weight: 800; color: var(--primary);
  line-height: 1.1; letter-spacing: -.5px; font-variant-numeric: tabular-nums;
}
.stat-card .num.sm { font-size: 22px; letter-spacing: 0; }
.stat-card .num small { font-size: 15px; font-weight: 700; margin-left: 4px; letter-spacing: 0; }
.stat-card .lbl { color: var(--sub); font-size: 13px; margin-top: 5px; }
.stat-ico {
  flex: none; width: 46px; height: 46px; border-radius: 13px;
  background: var(--soft); color: var(--accent);
  display: flex; align-items: center; justify-content: center;
}
.stat-ico svg { width: 24px; height: 24px; }
.stat-ico.warn { background: #FEF3E2; color: var(--warn); }

/* ---------------- 两栏 / 三栏 ---------------- */
.two-col { display: grid; grid-template-columns: 1fr 1fr; gap: 18px; }
.three-col { display: grid; grid-template-columns: 1fr 1fr 1fr; gap: 18px; }

/* ---------------- 今日课程时间线（对齐效果图 02：时间+科目，当前课描边气泡） ---------------- */
.timeline { padding-top: 4px; }
.tl-row {
  display: flex; align-items: center; gap: 0; min-height: 46px;
  border-bottom: 1px solid var(--border);
}
.tl-row:last-of-type { border-bottom: none; }
.tl-time {
  width: 74px; flex: none; font-size: 14px; color: var(--sub);
  font-variant-numeric: tabular-nums; text-align: left;
}
.tl-subject { font-size: 14.5px; font-weight: 500; color: var(--text); padding-left: 14px; }
.tl-row.done .tl-subject { color: var(--sub); font-weight: 400; }
.tl-row.now .tl-subject { color: var(--primary); font-weight: 700; }
.tl-now-dot {
  width: 9px; height: 9px; border-radius: 50%; flex: none; margin-left: 10px;
  background: var(--accent); box-shadow: 0 0 0 3px rgba(20,184,166,.18);
}
.tl-bubble {
  margin-left: 12px; font-size: 12.5px; font-weight: 600; color: var(--primary);
  background: var(--soft); border: 1px solid var(--soft-line); border-radius: 8px;
  padding: 7px 14px; white-space: nowrap;
  box-shadow: 0 2px 8px rgba(15,118,110,.08);
}

/* ---------------- 课堂总结列表 ---------------- */
.sum-list { display: flex; flex-direction: column; gap: 10px; }
.sum-item {
  display: flex; align-items: center; gap: 13px;
  border: 1px solid var(--border); border-radius: 12px; padding: 13px 14px;
  transition: border-color .15s, box-shadow .15s;
}
.sum-item:hover { border-color: var(--soft-line); box-shadow: 0 4px 14px rgba(15,23,42,.05); }
.sum-ico {
  flex: none; width: 42px; height: 42px; border-radius: 12px;
  background: var(--soft); color: var(--accent);
  display: flex; align-items: center; justify-content: center;
}
.sum-ico svg { width: 21px; height: 21px; }
.sum-info { flex: 1; min-width: 0; }
.sum-name {
  font-size: 14.5px; font-weight: 700; color: var(--ink);
  display: flex; align-items: center; gap: 8px; flex-wrap: wrap;
}
.sum-meta { color: var(--sub); font-size: 12.5px; margin-top: 3px; }

.tag { font-size: 11px; padding: 2px 9px; border-radius: 20px; font-weight: 700; flex: none; }
.tag.new { background: var(--soft); color: var(--primary); }
.tag.review { background: #EDE9FE; color: #6D28D9; }
.tag.exam { background: #FEF3C7; color: #B45309; }

/* ---------------- 按钮 ---------------- */
.btn {
  background: var(--primary); color: #fff; border: none; border-radius: 9px;
  padding: 9px 18px; font-size: 14px; font-weight: 600; font-family: inherit;
  cursor: pointer; transition: background .15s, box-shadow .15s; flex: none;
  display: inline-flex; align-items: center; justify-content: center; gap: 6px;
}
.btn:hover { background: var(--primary-deep); }
.btn.ghost { background: var(--card); color: var(--sub); border: 1px solid var(--border-strong); }
.btn.ghost:hover { background: var(--bg); color: var(--ink); border-color: var(--muted); }
.btn.sm { padding: 6px 15px; font-size: 12.5px; border-radius: 8px; }
.btn.primary.big { padding: 12px 40px; font-size: 15px; border-radius: 10px; }
.btn:disabled { opacity: .45; cursor: not-allowed; }
.btn:disabled:hover { background: var(--primary); }
.btn.ghost:disabled:hover { background: var(--card); }

/* ---------------- 导出目录（对齐效果图 02：灰色文件夹 + 日期，无框均布） ---------------- */
.dir-row { display: flex; gap: 14px; flex-wrap: wrap; }
.dir-item {
  display: flex; flex-direction: column; align-items: center; gap: 7px;
  cursor: pointer; padding: 14px 20px; min-width: 150px; flex: 1;
  transition: transform .15s;
}
.dir-item:hover { transform: translateY(-2px); }
.dir-ico { color: var(--border-strong); display: flex; transition: color .15s; }
.dir-ico svg { width: 40px; height: 40px; }
.dir-item:hover .dir-ico { color: var(--muted); }
.dir-date { font-size: 13.5px; font-weight: 600; color: var(--text); }

/* ---------------- 工具入口（设置页内承载已移出主导航的功能） ---------------- */
.tool-entry { display: grid; grid-template-columns: 1fr 1fr; gap: 12px; }
.te-btn {
  display: flex; align-items: center; gap: 12px; text-align: left;
  border: 1px solid var(--border); border-radius: 12px; background: var(--card);
  padding: 14px 16px; cursor: pointer; transition: border-color .15s, box-shadow .15s;
  font-family: inherit;
}
.te-btn:hover { border-color: var(--soft-line); box-shadow: 0 4px 14px rgba(15,23,42,.05); }
.te-ico {
  flex: none; width: 40px; height: 40px; border-radius: 11px;
  background: var(--soft); color: var(--accent);
  display: flex; align-items: center; justify-content: center;
}
.te-ico svg { width: 20px; height: 20px; }
.te-btn > span { font-size: 14px; font-weight: 700; color: var(--ink); display: block; }
.te-btn small { color: var(--sub); font-size: 11.5px; display: block; margin-top: 2px; }

/* ---------------- 课件上传 ---------------- */
.drop-zone {
  border: 2px dashed var(--border-strong); border-radius: 14px; background: var(--card);
  text-align: center; padding: 44px 20px; cursor: pointer;
  transition: border-color .18s, background .18s;
  display: flex; flex-direction: column; align-items: center;
}
.drop-zone:hover { border-color: var(--soft-line); }
.drop-zone.over { background: #F5FEFC; border-color: var(--accent); }
.dz-ico {
  width: 58px; height: 58px; border-radius: 50%; background: var(--soft); color: var(--accent);
  display: flex; align-items: center; justify-content: center; margin-bottom: 14px;
}
.dz-ico svg { width: 30px; height: 30px; }
.dz-title { font-size: 16.5px; font-weight: 700; color: var(--ink); }
.dz-hint { color: var(--sub); font-size: 13px; margin: 7px 0 16px; }

.subjects { display: flex; flex-direction: column; gap: 12px; }
.subj-label { color: var(--ink); font-size: 15px; font-weight: 700; }
.subj-row { display: flex; align-items: center; gap: 10px; flex-wrap: wrap; }
.subj-row .chip.add { margin-left: auto; }
.chip {
  border: 1px solid var(--border-strong); background: var(--card); color: var(--text);
  border-radius: 20px; padding: 8px 20px; font-size: 13.5px; font-family: inherit;
  cursor: pointer; transition: all .15s;
  display: inline-flex; align-items: center; gap: 5px;
}
.chip:hover { border-color: var(--soft-line); color: var(--primary); }
.chip small { opacity: .62; }
.chip.on {
  background: var(--primary); color: #fff; border-color: var(--primary); font-weight: 600;
  box-shadow: 0 3px 10px rgba(15,118,110,.22);
}
.chip.on small { opacity: .85; }
.chip.add { border-style: dashed; border-color: var(--soft-line); color: var(--primary); font-weight: 600; }
.chip.add:hover { background: var(--soft); border-style: solid; }
.chip.sm { padding: 5px 13px; font-size: 12.5px; }
.chip.warn-chip { background: #FEF3C7; border-color: #FCD34D; color: var(--warn); font-weight: 600; }
.chips { display: flex; gap: 8px; flex-wrap: wrap; }

/* 上传列表 + 进度条 */
.ul-row { display: flex; align-items: center; gap: 13px; padding: 12px 0; border-bottom: 1px solid var(--border); }
.ul-row:last-child { border-bottom: none; }
.ul-ico {
  flex: none; width: 38px; height: 38px; border-radius: 10px;
  background: var(--bg); color: var(--sub);
  display: flex; align-items: center; justify-content: center;
}
.ul-ico svg { width: 19px; height: 19px; }
.ul-main { flex: 1; min-width: 0; }
.ul-name {
  font-size: 14px; color: var(--ink); font-weight: 500;
  overflow: hidden; text-overflow: ellipsis; white-space: nowrap;
}
.ul-bar {
  height: 5px; border-radius: 3px; background: #EDF1F5; margin-top: 8px; overflow: hidden;
}
.ul-bar i { display: block; height: 100%; border-radius: 3px; background: var(--accent); transition: width .3s; }
.ul-state {
  flex: none; font-size: 13px; font-weight: 700; color: var(--primary);
  font-variant-numeric: tabular-nums; min-width: 56px; text-align: right;
}
.ul-state.done { color: var(--ok); }
.ul-state.done::before { content: '✓ '; }
.upload-foot { display: flex; flex-direction: column; align-items: center; gap: 16px; padding: 6px 0 4px; }
.upload-note { color: var(--sub); font-size: 13px; text-align: center; }

/* ---------------- 总结视图 ---------------- */
.sum-hero { display: flex; align-items: flex-start; justify-content: space-between; gap: 20px; }
.hero-title { font-size: 26px; font-weight: 800; color: var(--ink); letter-spacing: -.3px; line-height: 1.3; }
.hero-sub {
  color: var(--sub); font-size: 13.5px; margin-top: 8px;
  display: flex; align-items: center; gap: 10px; flex-wrap: wrap;
}
.hero-files { color: var(--muted); font-size: 12.5px; }
.done-badge {
  display: inline-flex; align-items: center; gap: 7px; flex: none;
  background: #E7F8EF; color: var(--ok); font-weight: 700; font-size: 13px;
  padding: 7px 15px; border-radius: 20px;
}
.done-badge .dot { width: 7px; height: 7px; border-radius: 50%; background: var(--ok); }

.quality-row {
  display: flex; align-items: center; gap: 12px; flex-wrap: wrap;
  background: var(--card); border: 1px solid var(--border); border-radius: 12px;
  padding: 13px 18px; box-shadow: var(--shadow);
}
.q-label { font-size: 14px; font-weight: 700; color: var(--ink); }
.stars { color: #F59E0B; letter-spacing: 4px; font-size: 18px; }
.dl-link {
  margin-left: auto; color: var(--primary); font-weight: 600; font-size: 13px;
  text-decoration: none; border-bottom: 1px dashed var(--primary); padding-bottom: 1px;
}

/* 老师总结：虚线建议框 */
.advice-group { display: flex; flex-direction: column; gap: 8px; }
.advice-group + .advice-group { margin-top: 8px; }
.advice-box {
  display: flex; align-items: flex-start; gap: 9px;
  border: 1px dashed var(--soft-line); border-radius: 10px;
  background: #FAFEFD; padding: 10px 12px;
}
.adv-ico { flex: none; color: var(--accent); display: flex; margin-top: 1px; }
.adv-ico svg { width: 15px; height: 15px; }
.adv-text { font-size: 13.5px; line-height: 1.65; color: var(--text); }
.adv-text b { color: var(--primary); font-weight: 700; }

/* 学生总结：重点 / 难点 */
.kp-group + .kp-group { margin-top: 16px; }
.kp-label {
  font-size: 13px; font-weight: 700; color: var(--primary); margin-bottom: 7px;
  display: flex; align-items: center; gap: 7px;
}
.kp-label::before { content: ''; width: 3px; height: 13px; border-radius: 2px; background: var(--accent); }
.kp-list { list-style: none; display: flex; flex-direction: column; gap: 6px; }
.kp-list li {
  font-size: 13.5px; line-height: 1.7; color: var(--text);
  position: relative; padding-left: 15px;
}
.kp-list li::before {
  content: ''; position: absolute; left: 2px; top: 9px;
  width: 5px; height: 5px; border-radius: 50%; background: var(--soft-line);
}

/* ---------------- 思维导图 ---------------- */
.mm-wrap { min-height: 260px; }
.mm-svg { width: 100%; height: 300px; display: block; }
.mm-fallback { max-height: 300px; overflow: auto; }
.mm-tip {
  font-size: 12px; color: var(--warn); background: #FEF3C7;
  border-radius: 8px; padding: 5px 10px; margin-bottom: 8px;
}
.mm-tree { font-size: 13px; }
.mm-node {
  border-left: 3px solid var(--primary); padding: 4px 9px; margin-bottom: 4px;
  border-radius: 0 6px 6px 0; background: var(--bg); line-height: 1.5;
}
.mm-node.lv0 { border-left-color: #0F766E; font-weight: 700; }
.mm-node.lv1 { border-left-color: #14B8A6; }
.mm-node.lv2 { border-left-color: #0EA5E9; }
.mm-node.lv3 { border-left-color: #F59E0B; }
.mm-node.lv4 { border-left-color: #94A3B8; }

/* ---------------- Markdown 正文排版 ---------------- */
.file-tabs { display: flex; align-items: center; gap: 8px; flex-wrap: wrap; }
.md-body { font-size: 14px; line-height: 1.85; color: var(--text); margin-top: 4px; }
.md-body h1 { font-size: 20px; font-weight: 800; color: var(--ink); margin: 18px 0 10px; }
.md-body h2 {
  font-size: 17px; font-weight: 700; color: var(--ink); margin: 20px 0 9px;
  padding-left: 11px; border-left: 3px solid var(--accent);
}
.md-body h3 { font-size: 15px; font-weight: 700; color: var(--ink); margin: 15px 0 7px; }
.md-body h4 { font-size: 14px; font-weight: 700; color: var(--primary); margin: 12px 0 6px; }
.md-body p { margin: 8px 0; }
.md-body ul, .md-body ol { margin: 8px 0 8px 22px; }
.md-body li { margin: 5px 0; }
.md-body blockquote {
  border-left: 3px solid var(--soft-line); background: var(--bg);
  padding: 9px 14px; margin: 12px 0; color: var(--sub); border-radius: 0 8px 8px 0;
}
.md-body code {
  background: #F1F5F9; border: 1px solid var(--border); border-radius: 5px;
  padding: 1px 6px; font-size: 12.5px; font-family: Consolas, "Courier New", monospace;
}
.md-body pre {
  background: #0F172A; color: #E2E8F0; border-radius: 10px;
  padding: 13px 15px; overflow: auto; margin: 12px 0;
}
.md-body pre code { background: none; border: none; color: inherit; padding: 0; font-size: 12.5px; }
.md-body hr { border: none; border-top: 1px solid var(--border); margin: 16px 0; }
.md-body table { width: 100%; border-collapse: collapse; margin: 12px 0; font-size: 13.5px; }
.md-body th, .md-body td { border: 1px solid var(--border); padding: 8px 11px; text-align: left; }
.md-body th { background: var(--bg); font-weight: 700; }
.md-body a { color: var(--primary); }
.md-body strong { color: var(--ink); font-weight: 700; }

/* ---------------- 分贝面积图 ---------------- */
.db-legend { font-size: 13px; color: var(--sub); }
.db-legend b { color: var(--primary); font-weight: 700; }
.db-chart { position: relative; }
.db-svg { width: 100%; height: auto; display: block; }
.grid-line { stroke: #EEF2F6; stroke-width: 1; }
.axis-txt { fill: var(--muted); font-size: 11px; font-family: inherit; }
.avg-line { stroke: #F59E0B; stroke-width: 1.4; stroke-dasharray: 5 4; opacity: .85; }
.db-foot { color: var(--muted); font-size: 12px; margin-top: 10px; }

/* ---------------- 表单 ---------------- */
.row { display: flex; gap: 12px; flex-wrap: wrap; align-items: center; }
.row + .row { margin-top: 10px; }
.model-grid { display: flex; gap: 10px; flex-wrap: wrap; }
.model-meta { color: var(--sub); font-size: 13px; margin-top: 12px; line-height: 1.8; }
.model-meta b { color: var(--ink); }
.model-meta b.ok { color: var(--ok); }
.model-meta b.bad { color: var(--danger); }
.input {
  flex: 1; min-width: 200px; border: 1px solid var(--border-strong); border-radius: 9px;
  padding: 10px 14px; font-size: 14px; outline: none; background: #fff;
  color: var(--ink); font-family: inherit; transition: border-color .15s, box-shadow .15s;
}
.input:focus { border-color: var(--accent); box-shadow: 0 0 0 3px rgba(20,184,166,.12); }
.input.select { flex: none; width: 168px; cursor: pointer; }
.input.area { width: 100%; resize: vertical; line-height: 1.7; }
.official-tip {
  margin-top: 16px; background: #F5FEFC; border: 1px solid var(--soft-line);
  border-radius: 10px; padding: 12px 15px; font-size: 13px; color: var(--text); line-height: 1.75;
}
.official-tip b { color: var(--primary); }
.chunk-badge {
  display: inline-block; margin-top: 12px; background: var(--soft); color: var(--primary);
  font-weight: 700; font-size: 13px; padding: 6px 15px; border-radius: 20px;
}

/* 步骤条（教科书引导） */
.step { display: flex; gap: 13px; padding: 14px 0; border-bottom: 1px solid var(--border); }
.step:last-of-type { border-bottom: none; }
.step-no {
  flex: none; width: 24px; height: 24px; border-radius: 50%;
  background: var(--primary); color: #fff; font-size: 12.5px; font-weight: 700;
  display: flex; align-items: center; justify-content: center; margin-top: 1px;
}
.step-body { flex: 1; min-width: 0; }
.step-title { font-size: 14px; font-weight: 700; color: var(--ink); margin-bottom: 10px; }

/* 硬件信息 */
.hw-grid { display: grid; grid-template-columns: repeat(auto-fit, minmax(180px, 1fr)); gap: 10px; margin-bottom: 12px; }
.hw-item {
  background: var(--bg); border-radius: 10px; padding: 10px 14px;
  display: flex; flex-direction: column; gap: 3px;
}
.hw-k { font-size: 12px; color: var(--sub); }
.hw-v { font-size: 14px; font-weight: 700; color: var(--ink); }

/* ---------------- 教科书 ---------------- */
.book-list { display: flex; flex-direction: column; gap: 7px; }
.book-row {
  display: flex; align-items: center; gap: 10px; padding: 9px 13px;
  border: 1px solid var(--border); border-radius: 10px; font-size: 13.5px; cursor: pointer;
  transition: border-color .15s, background .15s;
}
.book-row:hover { border-color: var(--soft-line); background: #FAFEFD; }
.book-row.static { cursor: default; background: var(--bg); }
.book-row.static:hover { border-color: var(--border); background: var(--bg); }
.book-row input[type="checkbox"] { accent-color: var(--primary); width: 15px; height: 15px; }
.bk-subj { font-weight: 700; min-width: 72px; color: var(--ink); }
.bk-ver { color: var(--sub); }
.tb-meta { display: flex; gap: 12px; flex-wrap: wrap; margin-bottom: 14px; }
.tb-meta span {
  font-size: 12.5px; color: var(--sub); background: var(--bg);
  border-radius: 8px; padding: 7px 13px;
}
.tb-meta b { color: var(--primary); font-weight: 700; margin-left: 4px; }
.answer-box { margin-top: 14px; background: var(--bg); border: 1px solid var(--border); border-radius: 10px; padding: 14px 18px; }
.src-list { margin-top: 16px; }
.src-head { font-size: 12.5px; font-weight: 700; color: var(--sub); margin-bottom: 8px; }
.src-item {
  border-left: 3px solid var(--accent); background: var(--bg); border-radius: 0 8px 8px 0;
  padding: 9px 13px; margin-bottom: 8px; font-size: 13px;
}
.src-book { font-weight: 700; color: var(--ink); }
.src-chap { color: var(--sub); margin: 0 8px; }
.src-score { color: var(--primary); font-size: 12px; font-weight: 600; }
.src-text { color: var(--text); margin-top: 5px; line-height: 1.7; white-space: pre-wrap; }

/* ---------------- 座位表 ---------------- */
.seat-grid { display: grid; gap: 9px; }
.seat {
  border: 1px solid var(--soft-line); border-radius: 10px; background: #F5FEFC;
  padding: 11px 6px; text-align: center; min-height: 60px;
  display: flex; flex-direction: column; justify-content: center; gap: 3px;
}
.seat.vacant { background: var(--bg); border-style: dashed; border-color: var(--border-strong); }
.seat-name { font-size: 13.5px; font-weight: 700; color: var(--ink); }
.seat.vacant .seat-name { color: #CBD5E1; font-weight: 500; }
.seat-idx { font-size: 11px; color: var(--muted); }

/* ---------------- 隧道规则 ---------------- */
.rule-list { display: flex; flex-direction: column; gap: 7px; }
.rule-row {
  display: flex; gap: 12px; align-items: center; font-size: 13px;
  background: var(--bg); border: 1px solid var(--border); border-radius: 8px; padding: 9px 13px;
}
.rule-name { font-weight: 700; min-width: 110px; color: var(--ink); }
.rule-val { color: var(--sub); word-break: break-all; }

/* ---------------- 响应式 ---------------- */
@media (max-width: 1080px) {
  .stat-grid { grid-template-columns: repeat(2, 1fr); }
  .two-col, .three-col { grid-template-columns: 1fr; }
}
@media (max-width: 720px) {
  .topbar { height: auto; flex-wrap: wrap; gap: 10px; padding: 10px 16px 0; }
  .brand { order: 1; }
  .run-badge { order: 2; margin-left: auto; }
  .nav { order: 3; flex: none; width: 100%; margin-left: 0; justify-content: flex-start; }
  .nav-btn { padding: 12px 11px; }
  .content { padding: 16px 16px 40px; }
  .stat-grid { grid-template-columns: 1fr 1fr; gap: 12px; }
  .stat-card { padding: 16px; }
  .stat-card .num { font-size: 26px; }
  .stat-ico { width: 38px; height: 38px; border-radius: 11px; }
  .stat-ico svg { width: 20px; height: 20px; }
  .hero-title { font-size: 21px; }
  .tl-bubble { display: none; }
}
</style>
