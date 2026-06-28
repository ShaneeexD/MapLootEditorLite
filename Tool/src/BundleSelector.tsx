import { useEffect, useMemo, useRef, useState } from 'react'
import { Search } from 'lucide-react'
import { Tooltip } from './Tooltip'
import { findBundleName, type BundleInfo } from './bundleApi'

export function BundleSelector({
  value,
  onChange,
  bundles,
  label = 'Prefab Path',
  tooltip,
}: {
  value: string
  onChange: (v: string) => void
  bundles: BundleInfo[] | null
  label?: string
  tooltip?: string
}) {
  const [query, setQuery] = useState(value || '')
  const [open, setOpen] = useState(false)
  const ref = useRef<HTMLDivElement>(null)

  useEffect(() => {
    setQuery(value || '')
  }, [value])

  useEffect(() => {
    function handleClick(e: MouseEvent) {
      if (ref.current && !ref.current.contains(e.target as Node)) {
        setOpen(false)
      }
    }
    document.addEventListener('mousedown', handleClick)
    return () => document.removeEventListener('mousedown', handleClick)
  }, [])

  const selectedName = useMemo(() => findBundleName(value, bundles), [value, bundles])

  const filtered = useMemo(() => {
    if (!bundles) return []
    const q = query.toLowerCase().trim()
    if (!q) return bundles.slice(0, 50)
    return bundles
      .filter((b) => b.name.toLowerCase().includes(q) || b.path.toLowerCase().includes(q))
      .slice(0, 50)
  }, [bundles, query])

  return (
    <div ref={ref} className="relative">
      <label className="label flex items-center">
        {label}
        {tooltip && <Tooltip text={tooltip} />}
      </label>
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
          onBlur={() => {
            setTimeout(() => setOpen(false), 150)
            onChange(query)
          }}
          placeholder={bundles ? 'Search bundle name or path' : 'Load bundle manifest in Bundles tab'}
          disabled={bundles === null}
        />
      </div>
      {selectedName && <div className="text-xs text-tarkov-text-dim mt-1">{selectedName}</div>}
      {open && bundles && (
        <div className="absolute z-10 mt-1 w-full max-h-60 overflow-y-auto bg-tarkov-surface border border-tarkov-border rounded shadow-lg">
          {filtered.length === 0 ? (
            <div className="px-3 py-2 text-sm text-tarkov-text-dim">No bundles found</div>
          ) : (
            <>
              {filtered.map((b) => (
                <button
                  key={b.path}
                  className="w-full text-left px-3 py-2 text-sm hover:bg-tarkov-border/50 text-tarkov-text"
                  onClick={() => {
                    onChange(b.path)
                    setQuery(b.path)
                    setOpen(false)
                  }}
                >
                  <div className="truncate">{b.name}</div>
                  <div className="text-xs text-tarkov-text-dim font-mono truncate">{b.path}</div>
                </button>
              ))}
              {filtered.length >= 50 && (
                <div className="px-3 py-1 text-xs text-tarkov-text-dim border-t border-tarkov-border">Showing first 50 results</div>
              )}
            </>
          )}
        </div>
      )}
    </div>
  )
}

export function BundleName({ path, bundles }: { path: string; bundles: BundleInfo[] | null }) {
  const name = useMemo(() => findBundleName(path, bundles), [path, bundles])
  if (!name) return null
  return <span className="text-xs text-tarkov-text-dim">{name}</span>
}
