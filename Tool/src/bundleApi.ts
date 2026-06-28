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
  return list
}

export async function loadDefaultBundles(): Promise<BundleInfo[]> {
  const response = await fetch('/bundles.json')
  if (!response.ok) {
    throw new Error(`Failed to load bundles: ${response.status}`)
  }
  const data = (await response.json()) as Array<{ path: string; name: string }>
  return data.map((d) => ({
    path: d.path,
    name: d.name,
    fileName: d.path,
    dependencies: [],
  }))
}

export function findBundleName(path: string, bundles: BundleInfo[] | null): string | null {
  if (!bundles || !path) return null
  const match = bundles.find((b) => b.path === path)
  return match?.name || null
}
