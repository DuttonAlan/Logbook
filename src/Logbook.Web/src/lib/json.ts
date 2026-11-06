// A number that JavaScript cannot represent exactly, kept as the text it was sent as. JSON.stringify writes it back as a
// number with these digits.
export interface RawJsonNumber {
  readonly rawJSON: string
}

// JSON.parse source text access, which TypeScript does not know yet.
interface JsonWithSource {
  rawJSON?: (text: string) => RawJsonNumber
  isRawJSON?: (value: unknown) => boolean
}

type SourceReviver = (key: string, value: unknown, context?: { source?: string }) => unknown

const { rawJSON, isRawJSON } = JSON as unknown as JsonWithSource

const keepExactNumbers: SourceReviver = (_key, value, context) => {
  const source = context?.source

  return typeof value === 'number' && rawJSON && source !== undefined && String(value) !== source ? rawJSON(source) : value
}

// Parses JSON without changing numbers. Properties are often 64-bit Ids, which a JavaScript number would round, so a
// filter on the value would no longer find the Log Event. Browsers without this feature fall back to rounding.
export function parseExactJson<T>(text: string): T {
  return JSON.parse(text, keepExactNumbers as (key: string, value: unknown) => unknown) as T
}

export function isRawJsonNumber(value: unknown): value is RawJsonNumber {
  return isRawJSON?.(value) ?? false
}
