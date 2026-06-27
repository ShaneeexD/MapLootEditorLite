export interface ItemInfo {
  _id: string
  _name: string
  _parent: string
  name: string
  shortName: string
}

let cache: ItemInfo[] | null = null
let loadingPromise: Promise<ItemInfo[]> | null = null

export async function loadItems(): Promise<ItemInfo[]> {
  if (cache) return cache
  if (loadingPromise) return loadingPromise

  loadingPromise = fetchDatabase()
  try {
    cache = await loadingPromise
    return cache
  } catch (err) {
    loadingPromise = null
    throw err
  }
}

async function fetchDatabase(): Promise<ItemInfo[]> {
  const [itemsRes, localesRes] = await Promise.all([
    fetch('https://db.sp-tarkov.com/api/cache/items'),
    fetch('https://db.sp-tarkov.com/api/cache/locales'),
  ])

  if (!itemsRes.ok) throw new Error(`Items API failed: ${itemsRes.status}`)
  if (!localesRes.ok) throw new Error(`Locales API failed: ${localesRes.status}`)

  const items = (await itemsRes.json()) as Record<string, { _id: string; _name: string; _parent: string }>
  const locales = (await localesRes.json()) as Record<string, Record<string, { Name: string; ShortName: string }>>

  const locale = locales.en || locales['en'] || Object.values(locales)[0] || {}

  return Object.values(items).map((item) => ({
    _id: item._id,
    _name: item._name,
    _parent: item._parent,
    name: locale[item._id]?.Name || item._name,
    shortName: locale[item._id]?.ShortName || '',
  }))
}

export function findItemName(itemId: string, items: ItemInfo[] | null): string {
  if (!items) return ''
  const item = items.find((i) => i._id === itemId)
  return item ? item.name : ''
}
