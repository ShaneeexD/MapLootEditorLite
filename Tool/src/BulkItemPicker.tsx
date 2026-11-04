import { useMemo, useState } from 'react'
import { Search } from 'lucide-react'
import { useItems } from './ItemSelector'

const MAX_VISIBLE = 100

export function BulkItemPicker({
  existingIds,
  onConfirm,
  onCancel,
}: {
  existingIds: string[]
  onConfirm: (ids: string[]) => void
  onCancel: () => void
}) {
  const items = useItems()
  const [query, setQuery] = useState('')
  // A Set keeps insertion order and makes duplicate selection impossible.
  const [selected, setSelected] = useState<Set<string>>(new Set())

  const existing = useMemo(() => new Set(existingIds), [existingIds])

  const filtered = useMemo(() => {
    if (!items) return []
    const q = query.toLowerCase().trim()
    const list = q
      ? items.filter((i) => i.name.toLowerCase().includes(q) || i.id.toLowerCase().includes(q))
      : items
    return list.slice(0, MAX_VISIBLE)
  }, [items, query])

  const toggle = (id: string) => {
    setSelected((prev) => {
      const next = new Set(prev)
      if (next.has(id)) next.delete(id)
      else next.add(id)
      return next
    })
  }

  return (
    <div className="border border-tarkov-border rounded p-3 space-y-2 bg-tarkov-surface">
      <div className="flex items-center gap-2">
        <Search size={16} className="text-tarkov-text-dim flex-shrink-0" />
        <input
          className="input-field"
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          placeholder={items ? 'Search item name or ID' : 'Loading items...'}
          disabled={items === null}
        />
      </div>
      <div className="max-h-60 overflow-y-auto border border-tarkov-border rounded">
        {filtered.length === 0 ? (
          <div className="px-3 py-2 text-sm text-tarkov-text-dim">No items found</div>
        ) : (
          filtered.map((item) => (
            <label
              key={item.id}
              className="flex items-center gap-2 px-3 py-1.5 text-sm hover:bg-tarkov-border/50 text-tarkov-text cursor-pointer"
            >
              <input type="checkbox" checked={selected.has(item.id)} onChange={() => toggle(item.id)} />
              <div className="min-w-0 flex-1">
                <div className="truncate">{item.name}</div>
                <div className="text-xs text-tarkov-text-dim font-mono truncate">{item.id}</div>
              </div>
              {existing.has(item.id) && <span className="text-xs text-tarkov-text-dim">already in zone</span>}
            </label>
          ))
        )}
      </div>
      <div className="text-xs text-tarkov-text-dim">
        Showing up to {MAX_VISIBLE} matches; selections are kept while you search.
      </div>
      <div className="flex items-center gap-2">
        <span className="text-sm text-tarkov-text-dim flex-1">{selected.size} selected</span>
        <button onClick={onCancel} className="btn-secondary text-sm">
          Cancel
        </button>
        <button
          onClick={() => onConfirm(Array.from(selected))}
          disabled={selected.size === 0}
          className="btn-primary text-sm disabled:opacity-50"
        >
          Add Selected ({selected.size})
        </button>
      </div>
    </div>
  )
}
