import { useEffect, useMemo, useRef, useState } from 'react'
import { Search } from 'lucide-react'
import { findItemName, loadItems, type ItemInfo } from './itemsApi'

export function ItemSelector({
  value,
  onChange,
  label = 'Item',
}: {
  value: string
  onChange: (id: string) => void
  label?: string
}) {
  const [items, setItems] = useState<ItemInfo[] | null>(null)
  const [query, setQuery] = useState('')
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    let mounted = true
    loadItems()
      .then((data) => mounted && setItems(data))
      .catch(() => mounted && setItems([]))
    return () => {
      mounted = false
    }
  }, [])

  useEffect(() => {
    const selectedName = findItemName(value, items)
    if (selectedName) setQuery(selectedName)
  }, [items, value])

  useEffect(() => {
    function handleClick(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        setOpen(false)
      }
    }
    document.addEventListener('mousedown', handleClick)
    return () => document.removeEventListener('mousedown', handleClick)
  }, [])

  const filtered = useMemo(() => {
    if (!items) return []
    const q = query.toLowerCase().trim()
    if (!q) return items.slice(0, 50)
    return items
      .filter((i) => i.name.toLowerCase().includes(q) || i._id.toLowerCase().includes(q))
      .slice(0, 50)
  }, [items, query])

  return (
    <div ref={ref} className="relative">
      {label && <label className="label">{label}</label>}
      <div className="flex items-center gap-2">
        <Search size={16} className="text-tarkov-text-dim flex-shrink-0" />
        <input
          className="input-field"
          value={query}
          onChange={(e) => {
            setQuery(e.target.value)
            setOpen(true)
          }}
          onFocus={() => setOpen(true)}
          placeholder={items ? 'Search item name or ID' : 'Loading items...'}
          disabled={items === null}
        />
      </div>
      {open && (
        <div className="absolute z-10 mt-1 w-full max-h-60 overflow-y-auto bg-tarkov-surface border border-tarkov-border rounded shadow-lg">
          {filtered.length === 0 ? (
            <div className="px-3 py-2 text-sm text-tarkov-text-dim">No items found</div>
          ) : (
            filtered.map((item) => (
              <button
                key={item._id}
                className="w-full text-left px-3 py-2 text-sm hover:bg-tarkov-border/50 text-tarkov-text"
                onClick={() => {
                  onChange(item._id)
                  setQuery(item.name)
                  setOpen(false)
                }}
              >
                <div className="truncate">{item.name}</div>
                <div className="text-xs text-tarkov-text-dim font-mono truncate">{item._id}</div>
              </button>
            ))
          )}
        </div>
      )}
    </div>
  )
}

export function ItemName({ id, items }: { id: string; items: ItemInfo[] | null }) {
  const name = useMemo(() => findItemName(id, items), [id, items])
  if (!name) return null
  return <span className="text-xs text-tarkov-text-dim">{name}</span>
}

export function useItems(): ItemInfo[] | null {
  const [items, setItems] = useState<ItemInfo[] | null>(null)

  useEffect(() => {
    let mounted = true
    loadItems()
      .then((data) => mounted && setItems(data))
      .catch(() => mounted && setItems([]))
    return () => {
      mounted = false
    }
  }, [])

  return items
}
