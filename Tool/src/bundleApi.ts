import propFilterText from './propfilter.txt?raw'

export interface BundleManifestEntry {
  FileName: string
  Crc: number
  Hash?: unknown
  Dependencies: string[]
}

export interface BundleInfo {
  path: string
  name: string
  fileName?: string
  dependencies?: string[]
  isEnvironmentProp?: boolean
}

export async function loadBundles(file: File): Promise<BundleInfo[]> {
  const text = await file.text()
  const manifest = JSON.parse(text) as Record<string, BundleManifestEntry>

  const list: BundleInfo[] = Object.entries(manifest).map(([path, entry]) => {
    const parts = path.split('/')
    return {
      path,
      name: parts[parts.length - 1] || path,
      fileName: entry.FileName,
      dependencies: entry.Dependencies || [],
    }
  })

  list.sort((a, b) => a.path.localeCompare(b.path))
  return categorizeBundles(list)
}

export async function loadDefaultBundles(): Promise<BundleInfo[]> {
  const response = await fetch('/bundles.json')
  if (!response.ok) {
    throw new Error(`Failed to load bundles: ${response.status}`)
  }
  const data = (await response.json()) as Array<{ path: string; name: string }>
  return categorizeBundles(
    data.map((d) => ({
      path: d.path,
      name: d.name,
      fileName: d.path,
      dependencies: [],
    }))
  )
}

export const BUNDLE_CATEGORIES = ['all', 'environment'] as const
export type BundleCategory = (typeof BUNDLE_CATEGORIES)[number]

const ENV_PROP_KEYWORDS = propFilterText
  .split(',')
  .map((w) => w.trim().replace(/^['"]+|['"]+$/g, ''))
  .filter((w) => w.length > 0)

const ENV_PROP_REGEX = (() => {
  const escaped = ENV_PROP_KEYWORDS.map((k) => k.replace(/[.*+?^${}()|[\]\\]/g, '\\$&'))
  return new RegExp(`\\b(?:${escaped.join('|')})\\b`, 'i')
})()

export function isEnvironmentProp(bundle: BundleInfo): boolean {
  const p = bundle.path.toLowerCase()
  if (p.startsWith('assets/content/location_objects/')) return true
  if (p.includes('/items/') || p.includes('/weapons/') || p.includes('/ammo/') || p.includes('/mods/')) return false
  return ENV_PROP_REGEX.test(p)
}

export function categorizeBundles(bundles: BundleInfo[]): BundleInfo[] {
  return bundles.map((b) => ({ ...b, isEnvironmentProp: isEnvironmentProp(b) }))
}

export function filterBundles(
  bundles: BundleInfo[],
  search: string,
  category: BundleCategory
): BundleInfo[] {
  let list = bundles
  if (category === 'environment') {
    list = list.filter((b) => b.isEnvironmentProp)
  }
  if (!search.trim()) return list
  const q = search.toLowerCase().trim()
  return list.filter((b) => b.name.toLowerCase().includes(q) || b.path.toLowerCase().includes(q))
}

export function findBundleName(path: string, bundles: BundleInfo[] | null): string | null {
  if (!bundles || !path) return null
  const match = bundles.find((b) => b.path === path)
  return match?.name || null
}
