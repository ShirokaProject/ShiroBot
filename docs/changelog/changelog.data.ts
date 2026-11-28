import { createContentLoader } from 'vitepress'

export interface ReleaseEntry {
  version: string
  date?: string
  unreleased: boolean
  summary: string
  url: string
}

declare const data: ReleaseEntry[]
export { data }

function compareVersions(a: string, b: string) {
  const pa = a.split('.').map(Number)
  const pb = b.split('.').map(Number)
  for (let i = 0; i < Math.max(pa.length, pb.length); i++) {
    const diff = (pb[i] ?? 0) - (pa[i] ?? 0)
    if (diff !== 0) return diff
  }
  return 0
}

// 每个版本一页：changelog/v<主>-<次>-<修订>.md，frontmatter 写 version、date（加引号，如 "2026-10-14"；未发布写 unreleased: true）和 summary。
export default createContentLoader('changelog/v*.md', {
  transform(pages): ReleaseEntry[] {
    return pages
      .map(({ url, frontmatter }) => ({
        version: String(frontmatter.version),
        date: frontmatter.date ? String(frontmatter.date) : undefined,
        unreleased: frontmatter.unreleased === true,
        summary: String(frontmatter.summary ?? ''),
        url
      }))
      .sort((a, b) => compareVersions(a.version, b.version))
  }
})
