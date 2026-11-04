<script setup lang="ts">
import { computed } from 'vue'
import { useData } from 'vitepress'

const { frontmatter } = useData()
const repo = 'https://github.com/ShirokaProject/ShiroBot'
const version = computed(() => String(frontmatter.value.version ?? ''))
const date = computed(() => String(frontmatter.value.date ?? ''))
const unreleased = computed(() => frontmatter.value.unreleased === true)
</script>

<template>
  <p class="release-meta">
    <span v-if="unreleased" class="release-badge">待发布</span>
    <span v-else>发布于 {{ date }}</span>
    <template v-if="!unreleased">
      · <a :href="`${repo}/releases/tag/v${version}`" target="_blank" rel="noreferrer">下载与发布页</a>
    </template>
    · <a href="./">全部版本</a>
  </p>
</template>

<style scoped>
.release-meta {
  margin-top: -8px;
  font-size: 14px;
  color: var(--vp-c-text-2);
}
.release-badge {
  font-size: 12px;
  padding: 0 8px;
  border-radius: 10px;
  background: var(--vp-c-brand-soft);
  color: var(--vp-c-brand-1);
}
</style>
