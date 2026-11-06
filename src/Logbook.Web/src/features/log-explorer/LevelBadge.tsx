import type { LogLevel } from '../../api/types.ts'

export function LevelBadge({ level }: { level: LogLevel }) {
  return <span className={`level-badge level-${level.toLowerCase()}`}>{level}</span>
}
