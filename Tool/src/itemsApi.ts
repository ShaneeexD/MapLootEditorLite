export interface ItemInfo {
  id: string
  name: string
  shortName?: string
  parentId: string | null
}

let cache: ItemInfo[] | null = null
let cacheMap: Map<string, ItemInfo> | null = null
let loadingPromise: Promise<ItemInfo[]> | null = null

export async function loadItems(): Promise<ItemInfo[]> {
  if (cache) return cache
  if (loadingPromise) return loadingPromise

  loadingPromise = fetchItemDb()
  try {
    cache = await loadingPromise
    cacheMap = new Map()
    for (const item of cache) {
      cacheMap.set(item.id, item)
    }
    return cache
  } catch (err) {
    loadingPromise = null
    throw err
  }
}

async function fetchItemDb(): Promise<ItemInfo[]> {
  const res = await fetch('/itemDb.json')
  if (!res.ok) throw new Error(`Failed to load itemDb.json: ${res.status}`)
  const data = await res.json() as ItemInfo[]
  return data
}

export function findItemName(itemId: string, items: ItemInfo[] | null): string {
  if (!items) return ''
  if (cacheMap) {
    const item = cacheMap.get(itemId)
    return item ? item.name : ''
  }
  const item = items.find((i) => i.id === itemId)
  return item ? item.name : ''
}


