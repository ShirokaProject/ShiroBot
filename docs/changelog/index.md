---
outline: false
---

<script setup>
import { data as releases } from './changelog.data'
</script>

# 更新日志

ShiroBot 宿主、`ShiroBot.SDK` / `ShiroBot.Templates` 包和内嵌 Dashboard 使用同一个版本号。点击版本查看完整的更新内容，每页右侧目录可跳转到破坏性变更、新功能、修复等分区。

各版本的 SDK 与 Model ABI 见 [ABI 兼容性与版本](/plugin/api-compatibility)，下载文件见 [GitHub Releases](https://github.com/ShirokaProject/ShiroBot/releases)。

<div class="changelog-list">
  <a v-for="r in releases" :key="r.version" class="changelog-item" :href="r.url">
    <div class="changelog-head">
      <span class="changelog-version">{{ r.version }}</span>
      <span v-if="r.unreleased" class="changelog-badge">待发布</span>
      <span v-else class="changelog-date">{{ r.date }}</span>
    </div>
    <p class="changelog-summary">{{ r.summary }}</p>
  </a>
</div>

<style scoped>
.changelog-list {
  display: grid;
  gap: 12px;
  margin-top: 24px;
}
.changelog-item {
  display: block;
  padding: 16px 20px;
  border: 1px solid var(--vp-c-divider);
  border-radius: 8px;
  color: inherit;
  text-decoration: none;
  transition: border-color 0.2s;
}
.changelog-item:hover {
  border-color: var(--vp-c-brand-1);
}
.changelog-head {
  display: flex;
  align-items: baseline;
  gap: 12px;
}
.changelog-version {
  font-size: 18px;
  font-weight: 600;
  color: var(--vp-c-brand-1);
}
.changelog-date {
  font-size: 14px;
  color: var(--vp-c-text-2);
}
.changelog-badge {
  font-size: 12px;
  padding: 0 8px;
  border-radius: 10px;
  background: var(--vp-c-brand-soft);
  color: var(--vp-c-brand-1);
}
.changelog-summary {
  margin: 6px 0 0;
  font-size: 14px;
  color: var(--vp-c-text-2);
  line-height: 1.6;
}
</style>
