<template>
  <figure class="mermaid-diagram" aria-label="流程图" :aria-busy="loading">
    <p v-if="loading" class="diagram-status" role="status">正在绘制流程图…</p>
    <div v-else-if="svg" class="diagram-content" v-html="svg" />
    <template v-else-if="error">
      <p class="diagram-error" role="alert">流程图绘制失败：{{ error }}</p>
      <pre>{{ code }}</pre>
    </template>
  </figure>
</template>

<script setup lang="ts">
import { onBeforeUnmount, onMounted, ref, useId, watch } from 'vue'
import { useData } from 'vitepress'

const props = defineProps<{ code: string }>()
const { isDark } = useData()
const id = `mermaid-${useId().replace(/[^a-zA-Z0-9_-]/g, '')}`
const svg = ref(''), error = ref(''), loading = ref(true)
let mounted = false, generation = 0

async function draw() {
  if (!mounted) return
  const current = ++generation
  loading.value = true
  error.value = ''
  try {
    // Mermaid needs the browser DOM; keep it out of SSR and load it only on diagram pages.
    const { default: mermaid } = await import('mermaid')
    if (!mounted || current !== generation) return
    await document.fonts.ready
    mermaid.initialize({
      startOnLoad: false,
      securityLevel: 'strict',
      theme: isDark.value ? 'dark' : 'default',
      fontFamily: 'var(--vp-font-family-base)',
      flowchart: { useMaxWidth: true, htmlLabels: false },
      sequence: {
        useMaxWidth: true,
        actorMargin: 32,
        width: 110,
        height: 36,
        diagramMarginX: 8,
        diagramMarginY: 8,
        boxMargin: 6,
        noteMargin: 6,
        messageMargin: 22,
        mirrorActors: false,
        actorFontSize: 14,
        messageFontSize: 13,
        noteFontSize: 13
      }
    })
    await mermaid.parse(props.code)
    if (!mounted || current !== generation) return
    const result = await mermaid.render(`${id}-${current}`, props.code)
    if (mounted && current === generation) svg.value = result.svg
  } catch (cause) {
    if (mounted && current === generation) {
      svg.value = ''
      error.value = cause instanceof Error ? cause.message : String(cause)
    }
  } finally {
    if (mounted && current === generation) loading.value = false
  }
}

watch([() => props.code, isDark], () => void draw())
onMounted(() => { mounted = true; void draw() })
onBeforeUnmount(() => { mounted = false; generation++ })
</script>

<style scoped>
.mermaid-diagram {
  max-width: 100%;
  margin: 20px 0;
  padding: 20px 12px;
  overflow-x: auto;
  border: 1px solid var(--vp-c-divider);
  border-radius: 12px;
  background: var(--vp-c-bg-soft);
}
.diagram-content { display: flex; justify-content: center; }
.diagram-content :deep(svg) {
  display: block;
  width: auto;
  max-width: 100%;
  max-height: 520px;
  height: auto;
}
.diagram-status { margin: 0; color: var(--vp-c-text-2); }
.diagram-error { color: var(--vp-c-danger-1); }
pre { white-space: pre-wrap; overflow-wrap: anywhere; }
</style>
