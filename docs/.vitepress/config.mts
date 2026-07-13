import { defineConfig } from 'vitepress'

export default defineConfig({
  lang: 'zh-CN',
  title: 'ShiroBot',
  description: 'ShiroBot 安装、插件开发与适配器开发文档',
  base: process.env.DOCS_BASE ?? '/',
  cleanUrls: true,
  lastUpdated: true,
  head: [
    ['meta', { name: 'theme-color', content: '#6750a4' }],
    ['meta', { name: 'color-scheme', content: 'light dark' }]
  ],
  themeConfig: {
    nav: [
      { text: '开始使用', link: '/guide/' },
      { text: '插件开发', link: '/plugin/' },
      { text: '适配器开发', link: '/adapter/' },
      {
        text: '生态',
        items: [
          { text: '插件与适配器列表', link: 'https://github.com/ShirokaProject/awesome-shirobot' },
          { text: 'GitHub 仓库', link: 'https://github.com/ShirokaProject/ShiroBot' }
        ]
      }
    ],
    sidebar: {
      '/guide/': [
        {
          text: '开始使用',
          items: [
            { text: '认识 ShiroBot', link: '/guide/' },
            { text: '安装与启动', link: '/guide/installation' },
            { text: '配置文件', link: '/guide/configuration' },
            { text: '运行与维护', link: '/guide/operations' }
          ]
        }
      ],
      '/plugin/': [
        {
          text: '插件开发',
          items: [
            { text: '创建第一个插件', link: '/plugin/' },
            { text: '消息路由与事件', link: '/plugin/routes-events' },
            { text: '上下文、配置与日志', link: '/plugin/context-config' },
            { text: '单 DLL 与 native 依赖', link: '/plugin/packaging-native' },
            { text: 'Avalonia 图片渲染', link: '/plugin/avalonia' }
          ]
        }
      ],
      '/adapter/': [
        {
          text: '适配器开发',
          items: [
            { text: '创建适配器', link: '/adapter/' },
            { text: '实现服务接口', link: '/adapter/services' },
            { text: '上报事件', link: '/adapter/events' },
            { text: '配置与部署', link: '/adapter/deployment' }
          ]
        }
      ]
    },
    socialLinks: [
      { icon: 'github', link: 'https://github.com/ShirokaProject/ShiroBot' }
    ],
    search: {
      provider: 'local',
      options: {
        translations: {
          button: {
            buttonText: '搜索文档',
            buttonAriaLabel: '搜索文档'
          },
          modal: {
            noResultsText: '没有找到相关内容',
            resetButtonTitle: '清除查询',
            footer: {
              selectText: '选择',
              navigateText: '切换',
              closeText: '关闭'
            }
          }
        }
      }
    },
    outline: {
      level: [2, 3],
      label: '本页目录'
    },
    docFooter: {
      prev: '上一篇',
      next: '下一篇'
    },
    lastUpdated: {
      text: '最后更新于',
      formatOptions: {
        dateStyle: 'medium',
        timeStyle: 'short'
      }
    },
    returnToTopLabel: '返回顶部',
    sidebarMenuLabel: '目录',
    darkModeSwitchLabel: '主题',
    lightModeSwitchTitle: '切换到浅色主题',
    darkModeSwitchTitle: '切换到深色主题',
    footer: {
      message: '基于 GNU GPL v3.0 许可证发布',
      copyright: 'Copyright © ShiroBot contributors'
    }
  }
})
