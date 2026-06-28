import { useMemo, useRef, useState, type ChangeEvent } from 'react'
import { FileJson, Search, Filter } from 'lucide-react'
import { loadBundles, filterBundles, type BundleInfo, type BundleCategory } from './bundleApi'

const MAX_RESULTS = 200

export function BundleBrowser({
  bundles,
  onLoad,
}: {
  bundles: BundleInfo[] | null
  onLoad: (bundles: BundleInfo[]) => void
}) {
  const [search, setSearch] = useState('')
  const [category, setCategory] = useState<BundleCategory>('all')
  const fileRef = useRef<HTMLInputElement>(null)

  const handleFile = async (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (!file) return
    try {
      const loaded = await loadBundles(file)
      onLoad(loaded)
    } catch (ex: any) {
      alert('Failed to load Windows.json: ' + (ex?.message || ex))
    }
  }

  const filtered = useMemo(() => {
    if (!bundles) return []
    return filterBundles(bundles, search, category).slice(0, MAX_RESULTS)
  }, [bundles, search, category])

  const totalMatching = useMemo(() => {
    if (!bundles) return 0
    return filterBundles(bundles, search, category).length
  }, [bundles, search, category])

  return (
    <div className="space-y-4">
      <div className="card space-y-4">
        <h3 className="text-sm font-semibold text-tarkov-accent uppercase tracking-wider">Bundle Browser</h3>
        <p className="text-sm text-tarkov-text-dim">
          Load the Windows.json manifest from your SPT installation to browse bundles and copy their paths.
        </p>
        <input
          type="file"
          accept=".json"
          ref={fileRef}
          className="hidden"
          onChange={handleFile}
        />
        <button
          onClick={() => fileRef.current?.click()}
          className="btn-secondary flex items-center gap-2"
        >
          <FileJson size={16} /> {bundles ? 'Load Different Manifest' : 'Load Windows.json'}
        </button>
        {bundles && (
          <div className="text-sm text-tarkov-text-dim">{bundles.length} bundles loaded</div>
        )}
      </div>

      {bundles && (
        <div className="space-y-2">
          <div className="flex items-center gap-2">
            <Search size={16} className="text-tarkov-text-dim" />
            <input
              className="input-field"
              value={search}
              onChange={(e) => setSearch(e.target.value)}
              placeholder="Search bundle name or path"
            />
          </div>
          <div className="flex items-center gap-2">
            <Filter size={16} className="text-tarkov-text-dim" />
            <select
              className="input-field"
              value={category}
              onChange={(e) => setCategory(e.target.value as BundleCategory)}
            >
              <option value="all">All bundles</option>
              <option value="environment">Environment props</option>
            </select>
          </div>
          <div className="text-xs text-tarkov-text-dim">
            {search || category !== 'all'
              ? `${totalMatching} matches (showing first ${Math.min(filtered.length, MAX_RESULTS)})`
              : `Showing first ${Math.min(filtered.length, MAX_RESULTS)} of ${bundles.length} bundles`}
          </div>
        </div>
      )}

      {bundles && (
        <div className="space-y-2">
          {filtered.map((b) => (
            <div
              key={b.path}
              className="card flex items-center justify-between gap-4"
            >
              <div className="min-w-0 flex-1">
                <div className="text-sm font-medium truncate">{b.name}</div>
                <div className="text-xs text-tarkov-text-dim font-mono truncate">{b.path}</div>
              </div>
              <button
                onClick={() => navigator.clipboard.writeText(b.path)}
                className="btn-secondary text-sm flex-shrink-0"
              >
                Copy
              </button>
            </div>
          ))}
          {filtered.length === MAX_RESULTS && (
            <p className="text-sm text-tarkov-text-dim">Showing first {MAX_RESULTS} results. Refine your search.</p>
          )}
        </div>
      )}
    </div>
  )
}
