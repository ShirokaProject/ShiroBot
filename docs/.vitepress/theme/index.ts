import DefaultTheme from 'vitepress/theme'
import type { Theme } from 'vitepress'
import MermaidDiagram from './MermaidDiagram.vue'
import ReleaseMeta from './ReleaseMeta.vue'

export default {
  extends: DefaultTheme,
  enhanceApp({ app }) {
    app.component('MermaidDiagram', MermaidDiagram)
    app.component('ReleaseMeta', ReleaseMeta)
  }
} satisfies Theme
