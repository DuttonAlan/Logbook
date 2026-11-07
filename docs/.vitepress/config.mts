import { posix } from 'node:path'
import { defineConfig } from 'vitepress'

const repository = 'https://github.com/DuttonAlan/Logbook'

export default defineConfig({
  title: 'Logbook',
  description:
    'A lightweight, self-hosted logging solution for .NET: log API, web interface and C# SDK, built on .NET, React and PostgreSQL.',
  lang: 'en-US',
  base: '/',
  cleanUrls: true,

  // The guides link to a local Logbook at http://localhost:….
  ignoreDeadLinks: 'localhostLinks',

  // docs/README.md is the index on GitHub; on the website it is the documentation overview, index.md is the homepage.
  rewrites: {
    'README.md': 'overview.md',
  },

  head: [
    ['link', { rel: 'icon', type: 'image/svg+xml', href: '/logo.svg' }],
    ['meta', { name: 'theme-color', content: '#c2410c' }],
  ],

  markdown: {
    config(md) {
      // Links that leave docs/ (CHANGELOG.md, SECURITY.md, Dockerfile, …) point to the file on GitHub.
      const linkOpen = md.renderer.rules.link_open
      md.renderer.rules.link_open = (tokens, idx, options, env, self) => {
        const href = tokens[idx].attrGet('href')
        if (href?.startsWith('../')) {
          const target = posix.normalize(posix.join('docs', posix.dirname(env.relativePath), href))
          if (!target.startsWith('docs/')) {
            tokens[idx].attrSet('href', `${repository}/blob/main/${target}`)
          }
        }
        return linkOpen ? linkOpen(tokens, idx, options, env, self) : self.renderToken(tokens, idx, options)
      }

      // Mermaid diagrams are rendered in the browser by the Mermaid component (theme/Mermaid.vue).
      const fence = md.renderer.rules.fence!
      md.renderer.rules.fence = (tokens, idx, options, env, self) => {
        const token = tokens[idx]
        if (token.info.trim() === 'mermaid') {
          return `<Mermaid code="${encodeURIComponent(token.content)}" />`
        }
        return fence(tokens, idx, options, env, self)
      }
    },
  },

  themeConfig: {
    logo: '/logo.svg',

    nav: [
      { text: 'Documentation', link: '/overview', activeMatch: '^/(overview|tutorials|how-to|reference|explanation)/?' },
      { text: 'Getting started', link: '/tutorials/getting-started' },
      { text: 'Roadmap', link: '/roadmap' },
      { text: 'Changelog', link: `${repository}/blob/main/CHANGELOG.md` },
    ],

    sidebar: [
      { text: 'Overview', link: '/overview' },
      {
        text: 'Tutorials',
        items: [{ text: 'Getting started', link: '/tutorials/getting-started' }],
      },
      {
        text: 'How-to guides',
        items: [
          { text: 'Run Logbook from source', link: '/how-to/run-from-source' },
          { text: 'Manage Applications and API Keys', link: '/how-to/manage-applications' },
          { text: 'Connect an application', link: '/how-to/connect-application' },
          { text: 'Search and follow logs', link: '/how-to/explore-logs' },
          { text: 'Configure sign-in', link: '/how-to/configure-sign-in' },
          { text: 'Deploy to production', link: '/how-to/deploy-production' },
          { text: 'Configure a reverse proxy', link: '/how-to/reverse-proxy' },
          { text: 'Upgrade Logbook', link: '/how-to/upgrade' },
          { text: 'Back up and restore', link: '/how-to/back-up-and-restore' },
        ],
      },
      {
        text: 'Reference',
        items: [
          { text: 'Configuration', link: '/reference/configuration' },
          { text: 'Admin API', link: '/reference/admin-api' },
          { text: 'Log API', link: '/reference/log-api' },
          { text: 'SDK behavior', link: '/reference/sdk-behavior' },
          { text: 'Health checks', link: '/reference/health-checks' },
        ],
      },
      {
        text: 'Explanation',
        items: [{ text: 'Architecture', link: '/explanation/architecture' }],
      },
      { text: 'Roadmap', link: '/roadmap' },
    ],

    socialLinks: [{ icon: 'github', link: repository }],

    search: { provider: 'local' },

    editLink: {
      pattern: `${repository}/edit/main/docs/:path`,
      text: 'Edit this page on GitHub',
    },

    outline: { level: [2, 3] },

    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright © Alan Dutton',
    },
  },
})
