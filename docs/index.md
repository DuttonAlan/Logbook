---
layout: home
title: Logbook
titleTemplate: Self-hosted logging for .NET

hero:
  name: Logbook
  text: Log. Search. Understand.
  tagline: A lightweight, self-hosted logging solution for .NET applications. Add one NuGet package, run one container, and see structured logs within minutes.
  image:
    src: /logo.svg
    alt: Logbook
  actions:
    - theme: brand
      text: Get started
      link: /tutorials/getting-started
    - theme: alt
      text: Roadmap
      link: /roadmap
    - theme: alt
      text: View on GitHub
      link: https://github.com/DuttonAlan/Logbook

features:
  - icon: 📥
    title: Log API
    details: Batched, compressed ingestion of structured log events, authenticated per Application with API Keys.
    link: /roadmap#m1-log-api-✅
    linkText: Milestone M1
  - icon: 🧩
    title: C# SDK
    details: An ILogger provider that buffers and sends in the background, and never blocks or crashes your application.
    link: /roadmap#m2-c-sdk-✅
    linkText: Milestone M2
  - icon: 🔎
    title: Web interface
    details: Search and filter by time, level, Application and text, see properties and exceptions, and follow a live tail.
    link: /roadmap#m3-web-interface-log-explorer
    linkText: Milestone M3
  - icon: 🏠
    title: Self-hosted
    details: One container and one PostgreSQL database. Retention per Application keeps it small.
    link: /explanation/architecture
    linkText: Architecture
---
