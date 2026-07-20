import { useEffect, useMemo, useRef, useState, type ChangeEvent, type ReactNode } from 'react'
import {
  Archive,
  Box,
  Crosshair,
  DoorOpen,
  Download,
  ExternalLink,
  FileJson,
  MapPin,
  Menu,
  Package,
  Plus,
  Store,
  Sun,
  Target,
  Trash2,
  Users,
  X,
  Zap,
} from 'lucide-react'
import { saveAs } from 'file-saver'
import { ClipboardPaste } from 'lucide-react'
import { ItemSelector } from './ItemSelector'
import { BundleBrowser } from './BundleBrowser'
import { BundleSelector } from './BundleSelector'
import { Tooltip } from './Tooltip'
import { type BundleInfo, loadDefaultBundles } from './bundleApi'
import {
  type InteractiveObject,
  type InteractiveObjectItem,
  InteractiveObjectType,
  ContainerLootMode,
  type LootItem,
  type LootZone,
  type LooseLootSpawn,
  LOOT_CONTAINER_TEMPLATES,
  type MapData,
  type PackData,
  type StaticObject,
  type TransformData,
  ZoneShape,
  defaultInteractiveObject,
  defaultLootItem,
  defaultMapData,
  defaultPackData,
  defaultTransform,
  generateContainerId,
  generateId,
  MAP_OPTIONS,
} from './types'
import {
  ExtractZoneList,
  LightZoneList,
  BotSpawnPointList,
  BotSpawnZoneList,
  PmcSpawnZoneList,
  WttQuestZoneList,
  WttStaticObjectList,
  TriggerZoneList,
} from './MarkerLists'

type MarkerTab =
  | 'spawns'
  | 'zones'
  | 'objects'
  | 'interactive'
  | 'extracts'
  | 'lights'
  | 'botpoints'
  | 'botzones'
  | 'pmczones'
  | 'wttquests'
  | 'wttobjects'
  | 'triggers'
  | 'bundles'

function migratePackData(pack: PackData): PackData {
  const maps: Record<string, MapData> = {}
  for (const [key, map] of Object.entries(pack.maps)) {
    maps[key] = {
      ...map,
      lootSpawns: map.lootSpawns.map((spawn) => ({
        ...spawn,
        items: migrateItems(spawn.items, (spawn as any).itemTpls),
      })),
      lootZones: map.lootZones.map((zone) => ({
        ...zone,
        scale: zone.scale ?? { x: 1, y: 1, z: 1 },
        shape: (zone as any).shape ?? ZoneShape.Sphere,
        items: migrateItems(zone.items, (zone as any).itemTpls),
      })),
      interactiveObjects: migrateInteractiveObjects(map.interactiveObjects ?? []),
      objects: map.objects ?? [],
      wttQuestZones: map.wttQuestZones ?? [],
      wttStaticObjects: map.wttStaticObjects ?? [],
      extractZones: map.extractZones ?? [],
      botSpawnPoints: map.botSpawnPoints ?? [],
      botSpawnZones: map.botSpawnZones ?? [],
      pmcSpawnZones: map.pmcSpawnZones ?? [],
      lightZones: map.lightZones ?? [],
      triggerZones: map.triggerZones ?? [],
    }
  }
  return { ...pack, maps }
}

const defaultInteractiveObjectItem: InteractiveObjectItem = {
  template: '',
  chance: 100,
  count: 1,
  questOnly: false,
  questCompleted: false,
  questId: '',
}

function migrateInteractiveObjects(objects: InteractiveObject[]): InteractiveObject[] {
  return objects.map((obj) => ({
    ...defaultInteractiveObject(),
    ...obj,
    items: (obj.items ?? []).map((item) => ({ ...defaultInteractiveObjectItem, ...item })),
  }))
}

function migrateItems(items: LootItem[] | undefined, itemTpls: string[] | undefined): LootItem[] {
  if (items && items.length > 0) {
    return items.map((item) => ({
      ...defaultLootItem(),
      ...item,
      rotation: item.rotation ?? defaultTransform(),
      randomRotation: item.randomRotation ?? true,
    }))
  }
  if (itemTpls && itemTpls.length > 0) {
    return itemTpls.map((t) => ({ ...defaultLootItem(), template: t || '', chance: 100 }))
  }
  return [defaultLootItem()]
}

export default function App() {
  const [pack, setPack] = useState<PackData>(defaultPackData())
  const [selectedMapId, setSelectedMapId] = useState<string>('')
  const [tab, setTab] = useState<MarkerTab>('spawns')
  const [newMapId, setNewMapId] = useState<string>(MAP_OPTIONS[0].id)
  const [sidebarOpen, setSidebarOpen] = useState(false)
  const [bundles, setBundles] = useState<BundleInfo[] | null>(null)
  const importRef = useRef<HTMLInputElement>(null)
  const [importKey, setImportKey] = useState(0)

  useEffect(() => {
    let mounted = true
    loadDefaultBundles()
      .then((data) => mounted && setBundles(data))
      .catch((err) => console.warn('Failed to load default bundle manifest:', err))
    return () => {
      mounted = false
    }
  }, [])

  const mapIds = useMemo(() => Object.keys(pack.maps), [pack.maps])
  const currentMap = selectedMapId ? pack.maps[selectedMapId] : null

  const updatePack = (updates: Partial<PackData>) => {
    setPack((p) => ({ ...p, ...updates }))
  }

  const updateMap = (mapId: string, updater: (m: MapData) => MapData) => {
    setPack((p) => {
      const map = p.maps[mapId]
      if (!map) return p
      return { ...p, maps: { ...p.maps, [mapId]: updater(map) } }
    })
  }

  const addMap = () => {
    if (!pack.maps[newMapId]) {
      updatePack({
        maps: { ...pack.maps, [newMapId]: defaultMapData(newMapId) },
      })
      setSelectedMapId(newMapId)
    }
  }

  const removeMap = (mapId: string) => {
    setPack((p) => {
      const next = { ...p.maps }
      delete next[mapId]
      return { ...p, maps: next }
    })
    if (selectedMapId === mapId) setSelectedMapId('')
  }

  const handleImport = (e: ChangeEvent<HTMLInputElement>) => {
    const file = e.target.files?.[0]
    if (!file) return
    const reader = new FileReader()
    reader.onload = () => {
      try {
        const imported = migratePackData(JSON.parse(reader.result as string) as PackData)
        setPack(imported)
        const first = Object.keys(imported.maps)[0] || ''
        setSelectedMapId(first)
        setImportKey((k) => k + 1)
      } catch {
        alert('Invalid pack JSON. Make sure it was exported from the in-game editor.')
      }
    }
    reader.readAsText(file)
  }

  const downloadPack = () => {
    const blob = new Blob([JSON.stringify(pack, null, 2)], { type: 'application/json' })
    const fileName = pack.name.trim().replace(/\s+/g, '_') || 'pack'
    saveAs(blob, `${fileName}.json`)
  }

  return (
    <div className="min-h-screen bg-tarkov-bg text-tarkov-text flex">
      <Sidebar open={sidebarOpen} onClose={() => setSidebarOpen(false)} />

      <div className="flex-1 flex flex-col">
        <header className="border-b border-tarkov-border bg-tarkov-surface px-6 py-4 flex items-center gap-4">
          <button
            onClick={() => setSidebarOpen(true)}
            className="p-2 rounded hover:bg-tarkov-border/50 text-tarkov-text-dim hover:text-tarkov-text transition-colors"
            aria-label="Open menu"
          >
            <Menu size={22} />
          </button>
          <div className="flex items-center gap-2 text-tarkov-accent">
            <Package size={24} />
            <h1 className="text-xl font-bold">Map Editor Lite Tool</h1>
          </div>
          <div className="flex-1" />
          <input
            key={importKey}
            ref={importRef}
            type="file"
            accept=".json"
            className="hidden"
            onChange={handleImport}
          />
          <button onClick={() => importRef.current?.click()} className="btn-secondary flex items-center gap-2">
            <FileJson size={16} /> Import Pack
          </button>
          <button onClick={downloadPack} className="btn-primary flex items-center gap-2">
            <Download size={16} /> Export Pack
          </button>
        </header>

        <main className="flex-1 flex overflow-hidden">
          <aside className="w-72 border-r border-tarkov-border bg-tarkov-surface flex flex-col">
            <div className="p-4 border-b border-tarkov-border space-y-3">
              <h2 className="text-sm font-semibold text-tarkov-accent uppercase tracking-wider">Pack Info</h2>
              <div>
                <label className="label flex items-center">
                  Name
                  <Tooltip text="Name of the exported pack." />
                </label>
                <input
                  className="input-field"
                  value={pack.name}
                  onChange={(e) => updatePack({ name: e.target.value })}
                />
              </div>
              <div>
                <label className="label flex items-center">
                  Author
                  <Tooltip text="Author of the pack." />
                </label>
                <input
                  className="input-field"
                  value={pack.author}
                  onChange={(e) => updatePack({ author: e.target.value })}
                />
              </div>
              <div>
                <label className="label flex items-center">
                  Version
                  <Tooltip text="Version of the pack." />
                </label>
                <input
                  className="input-field"
                  value={pack.version}
                  onChange={(e) => updatePack({ version: e.target.value })}
                />
              </div>
            </div>

            <div className="p-4 flex-1 overflow-y-auto">
              <h2 className="text-sm font-semibold text-tarkov-accent uppercase tracking-wider mb-3">Maps</h2>
              {mapIds.length === 0 ? (
                <p className="text-sm text-tarkov-text-dim">No maps yet. Import a pack exported in-game or add a map below.</p>
              ) : (
                <ul className="space-y-2">
                  {mapIds.map((id) => {
                    const label = MAP_OPTIONS.find((m) => m.id === id)?.label || id
                    return (
                      <li key={id}>
                        <button
                          onClick={() => setSelectedMapId(id)}
                          className={`w-full text-left px-3 py-2 rounded-lg border text-sm transition-colors flex items-center justify-between ${
                            selectedMapId === id
                              ? 'bg-tarkov-accent/20 border-tarkov-accent/50 text-tarkov-accent'
                              : 'bg-tarkov-bg border-tarkov-border text-tarkov-text hover:border-tarkov-accent'
                          }`}
                        >
                          <span>{label}</span>
                          <button
                            onClick={(e) => {
                              e.stopPropagation()
                              removeMap(id)
                            }}
                            className="p-1 hover:text-tarkov-error transition-colors"
                            aria-label="Remove map"
                          >
                            <Trash2 size={14} />
                          </button>
                        </button>
                      </li>
                    )
                  })}
                </ul>
              )}

              <div className="mt-4 pt-4 border-t border-tarkov-border space-y-2">
                <label className="label flex items-center">
                  Add Map
                  <Tooltip text="Add a map to this pack to begin editing its spawns, zones, and objects." />
                </label>
                <select className="input-field" value={newMapId} onChange={(e) => setNewMapId(e.target.value)}>
                  {MAP_OPTIONS.map((m) => (
                    <option key={m.id} value={m.id}>
                      {m.label}
                    </option>
                  ))}
                </select>
                <button onClick={addMap} className="btn-secondary w-full flex items-center justify-center gap-2">
                  <Plus size={16} /> Add Map
                </button>
              </div>
            </div>
          </aside>

          <section className="flex-1 flex flex-col overflow-hidden bg-tarkov-bg">
            <div className="px-6 py-4 border-b border-tarkov-border flex items-center justify-between">
              <div className="flex items-center gap-2">
                <span className="text-sm text-tarkov-text-dim">Map:</span>
                <span className="font-semibold text-tarkov-accent">
                  {MAP_OPTIONS.find((m) => m.id === selectedMapId)?.label || selectedMapId || 'none'}
                </span>
              </div>
              <div className="flex gap-2 flex-wrap">
                <TabButton
                  active={tab === 'spawns'}
                  onClick={() => setTab('spawns')}
                  icon={<Crosshair size={16} />}
                  label={`Spawns${currentMap ? ` (${currentMap.lootSpawns.length})` : ''}`}
                  disabled={!currentMap}
                />
                <TabButton
                  active={tab === 'zones'}
                  onClick={() => setTab('zones')}
                  icon={<MapPin size={16} />}
                  label={`Zones${currentMap ? ` (${currentMap.lootZones.length})` : ''}`}
                  disabled={!currentMap}
                />
                <TabButton
                  active={tab === 'objects'}
                  onClick={() => setTab('objects')}
                  icon={<Box size={16} />}
                  label={`Objects${currentMap ? ` (${currentMap.objects.length})` : ''}`}
                  disabled={!currentMap}
                />
                <TabButton
                  active={tab === 'interactive'}
                  onClick={() => setTab('interactive')}
                  icon={<Target size={16} />}
                  label={`Interactive${currentMap ? ` (${currentMap.interactiveObjects.length})` : ''}`}
                  disabled={!currentMap}
                />
                <TabButton
                  active={tab === 'extracts'}
                  onClick={() => setTab('extracts')}
                  icon={<DoorOpen size={16} />}
                  label={`Extracts${currentMap ? ` (${currentMap.extractZones.length})` : ''}`}
                  disabled={!currentMap}
                />
                <TabButton
                  active={tab === 'lights'}
                  onClick={() => setTab('lights')}
                  icon={<Sun size={16} />}
                  label={`Lights${currentMap ? ` (${currentMap.lightZones.length})` : ''}`}
                  disabled={!currentMap}
                />
                <TabButton
                  active={tab === 'botpoints'}
                  onClick={() => setTab('botpoints')}
                  icon={<Users size={16} />}
                  label={`Bot Points${currentMap ? ` (${currentMap.botSpawnPoints.length})` : ''}`}
                  disabled={!currentMap}
                />
                <TabButton
                  active={tab === 'botzones'}
                  onClick={() => setTab('botzones')}
                  icon={<Users size={16} />}
                  label={`Bot Zones${currentMap ? ` (${currentMap.botSpawnZones.length})` : ''}`}
                  disabled={!currentMap}
                />
                <TabButton
                  active={tab === 'pmczones'}
                  onClick={() => setTab('pmczones')}
                  icon={<Users size={16} />}
                  label={`PMC Zones${currentMap ? ` (${currentMap.pmcSpawnZones.length})` : ''}`}
                  disabled={!currentMap}
                />
                <TabButton
                  active={tab === 'wttquests'}
                  onClick={() => setTab('wttquests')}
                  icon={<MapPin size={16} />}
                  label={`WTT Quests${currentMap ? ` (${currentMap.wttQuestZones.length})` : ''}`}
                  disabled={!currentMap}
                />
                <TabButton
                  active={tab === 'wttobjects'}
                  onClick={() => setTab('wttobjects')}
                  icon={<Box size={16} />}
                  label={`WTT Objects${currentMap ? ` (${currentMap.wttStaticObjects.length})` : ''}`}
                  disabled={!currentMap}
                />
                <TabButton
                  active={tab === 'triggers'}
                  onClick={() => setTab('triggers')}
                  icon={<Zap size={16} />}
                  label={`Triggers${currentMap ? ` (${currentMap.triggerZones.length})` : ''}`}
                  disabled={!currentMap}
                />
                <TabButton
                  active={tab === 'bundles'}
                  onClick={() => setTab('bundles')}
                  icon={<Archive size={16} />}
                  label="Bundles"
                />
              </div>
            </div>

            <div className="flex-1 overflow-y-auto p-6">
              {tab === 'spawns' &&
                (currentMap ? (
                  <SpawnList
                    data={currentMap.lootSpawns}
                    onChange={(spawns) => updateMap(selectedMapId, (m) => ({ ...m, lootSpawns: spawns }))}
                  />
                ) : (
                  <NoMapMessage />
                ))}
              {tab === 'zones' &&
                (currentMap ? (
                  <ZoneList
                    data={currentMap.lootZones}
                    onChange={(zones) => updateMap(selectedMapId, (m) => ({ ...m, lootZones: zones }))}
                  />
                ) : (
                  <NoMapMessage />
                ))}
              {tab === 'objects' &&
                (currentMap ? (
                  <ObjectList
                    data={currentMap.objects}
                    onChange={(objects) => updateMap(selectedMapId, (m) => ({ ...m, objects }))}
                    bundles={bundles}
                  />
                ) : (
                  <NoMapMessage />
                ))}
              {tab === 'interactive' &&
                (currentMap ? (
                  <InteractiveObjectList
                    data={currentMap.interactiveObjects}
                    onChange={(interactiveObjects) => updateMap(selectedMapId, (m) => ({ ...m, interactiveObjects }))}
                  />
                ) : (
                  <NoMapMessage />
                ))}
              {tab === 'extracts' &&
                (currentMap ? (
                  <ExtractZoneList
                    data={currentMap.extractZones}
                    onChange={(zones) => updateMap(selectedMapId, (m) => ({ ...m, extractZones: zones }))}
                  />
                ) : (
                  <NoMapMessage />
                ))}
              {tab === 'lights' &&
                (currentMap ? (
                  <LightZoneList
                    data={currentMap.lightZones}
                    onChange={(zones) => updateMap(selectedMapId, (m) => ({ ...m, lightZones: zones }))}
                  />
                ) : (
                  <NoMapMessage />
                ))}
              {tab === 'botpoints' &&
                (currentMap ? (
                  <BotSpawnPointList
                    data={currentMap.botSpawnPoints}
                    onChange={(points) => updateMap(selectedMapId, (m) => ({ ...m, botSpawnPoints: points }))}
                  />
                ) : (
                  <NoMapMessage />
                ))}
              {tab === 'botzones' &&
                (currentMap ? (
                  <BotSpawnZoneList
                    data={currentMap.botSpawnZones}
                    onChange={(zones) => updateMap(selectedMapId, (m) => ({ ...m, botSpawnZones: zones }))}
                  />
                ) : (
                  <NoMapMessage />
                ))}
              {tab === 'pmczones' &&
                (currentMap ? (
                  <PmcSpawnZoneList
                    data={currentMap.pmcSpawnZones}
                    onChange={(zones) => updateMap(selectedMapId, (m) => ({ ...m, pmcSpawnZones: zones }))}
                  />
                ) : (
                  <NoMapMessage />
                ))}
              {tab === 'wttquests' &&
                (currentMap ? (
                  <WttQuestZoneList
                    data={currentMap.wttQuestZones}
                    onChange={(zones) => updateMap(selectedMapId, (m) => ({ ...m, wttQuestZones: zones }))}
                  />
                ) : (
                  <NoMapMessage />
                ))}
              {tab === 'wttobjects' &&
                (currentMap ? (
                  <WttStaticObjectList
                    data={currentMap.wttStaticObjects}
                    onChange={(objects) => updateMap(selectedMapId, (m) => ({ ...m, wttStaticObjects: objects }))}
                  />
                ) : (
                  <NoMapMessage />
                ))}
              {tab === 'triggers' &&
                (currentMap ? (
                  <TriggerZoneList
                    data={currentMap.triggerZones}
                    onChange={(zones) => updateMap(selectedMapId, (m) => ({ ...m, triggerZones: zones }))}
                  />
                ) : (
                  <NoMapMessage />
                ))}
              {tab === 'bundles' && <BundleBrowser bundles={bundles} onLoad={setBundles} />}
            </div>
          </section>
        </main>
      </div>
    </div>
  )
}

function NoMapMessage() {
  return (
    <div className="flex-1 flex flex-col items-center justify-center text-tarkov-text-dim p-8">
      <Package size={48} className="mb-4 text-tarkov-accent/50" />
      <p className="text-lg font-medium">Import a pack or select a map to get started.</p>
      <p className="text-sm mt-2 max-w-md text-center">
        This tool is designed to manage and refine the loot packs you export from the in-game editor.
        Set spawn chances, remove unwanted markers, and export a clean pack for distribution.
      </p>
    </div>
  )
}

function Sidebar({ open, onClose }: { open: boolean; onClose: () => void }) {
  return (
    <>
      {open && <div className="fixed inset-0 bg-black/50 z-40" onClick={onClose} />}
      <div
        className={`fixed top-0 left-0 h-full w-64 bg-tarkov-surface border-r border-tarkov-border z-50 transform transition-transform duration-200 ${
          open ? 'translate-x-0' : '-translate-x-full'
        }`}
      >
        <div className="flex items-center justify-between px-4 py-4 border-b border-tarkov-border">
          <div className="flex items-center gap-2 text-tarkov-accent">
            <Package size={22} />
            <span className="font-bold">Serenity Mods</span>
          </div>
          <button
            onClick={onClose}
            className="p-1 rounded hover:bg-tarkov-border/50 text-tarkov-text-dim hover:text-tarkov-text transition-colors"
            aria-label="Close menu"
          >
            <X size={20} />
          </button>
        </div>
        <nav className="p-2 space-y-1">
          <SidebarLink
            href="https://maplooteditorlite-tool.netlify.app"
            icon={<Package size={18} />}
            label="Map Editor Lite Tool"
            active
          />
          <SidebarLink href="https://tradergen-tool.netlify.app" icon={<Store size={18} />} label="TraderGen Tool" />
          <SidebarLink href="https://ammogen-tool.netlify.app" icon={<Target size={18} />} label="AmmoGen Tool" />
        </nav>
      </div>
    </>
  )
}

function SidebarLink({
  href,
  icon,
  label,
  active,
}: {
  href: string
  icon: ReactNode
  label: string
  active?: boolean
}) {
  return (
    <a
      href={href}
      target="_blank"
      rel="noopener noreferrer"
      className={`flex items-center gap-3 px-3 py-2.5 rounded-lg text-sm transition-colors ${
        active
          ? 'bg-tarkov-accent/20 text-tarkov-accent border border-tarkov-accent/50'
          : 'text-tarkov-text hover:bg-tarkov-border/50 hover:text-tarkov-text'
      }`}
    >
      {icon}
      <span className="flex-1">{label}</span>
      <ExternalLink size={14} className="text-tarkov-text-dim" />
    </a>
  )
}

function TabButton({
  active,
  onClick,
  icon,
  label,
  disabled,
}: {
  active: boolean
  onClick: () => void
  icon: ReactNode
  label: string
  disabled?: boolean
}) {
  return (
    <button
      onClick={disabled ? undefined : onClick}
      disabled={disabled}
      className={`flex items-center gap-2 px-4 py-2 rounded-lg text-sm font-medium transition-colors border disabled:opacity-50 disabled:cursor-not-allowed ${
        active
          ? 'bg-tarkov-accent/20 border-tarkov-accent/50 text-tarkov-accent'
          : 'bg-tarkov-surface border-tarkov-border text-tarkov-text hover:border-tarkov-accent'
      }`}
    >
      {icon}
      {label}
    </button>
  )
}

function ItemListEditor({
  value,
  onChange,
  showRotation = false,
}: {
  value: LootItem[]
  onChange: (items: LootItem[]) => void
  showRotation?: boolean
}) {
  const update = (index: number, updates: Partial<LootItem>) => {
    onChange(replaceAt(value, index, { ...value[index], ...updates }))
  }

  return (
    <div className="space-y-2">
      <label className="label flex items-center">
        Items (chance does not need to add to 100)
        <Tooltip text="List of items that can spawn here. Percent values are relative and do not need to sum to 100." />
      </label>
      {value.map((item, i) => (
        <div key={i} className="space-y-2">
          <div className="flex items-center gap-2">
            <div className="flex-1">
              <ItemSelector label="" value={item.template} onChange={(v) => update(i, { template: v })} tooltip="Item template ID that can spawn." />
            </div>
            <div className="w-28">
              <NumberField label="%" value={item.chance} onChange={(v) => update(i, { chance: v })} min={0} max={100} tooltip="Relative chance for this item to be selected." />
            </div>
            <button onClick={() => onChange(removeAt(value, i))} className="btn-danger p-2">
              <Trash2 size={16} />
            </button>
          </div>
          <div className="flex items-center gap-2 pl-2">
            <Toggle
              label="Quest only"
              checked={item.questOnly ?? false}
              onChange={(v) => update(i, { questOnly: v })}
              tooltip="Only spawn this item when the specified quest is active."
            />
            {item.questOnly && (
              <div className="flex-1">
                <TextField
                  label="Quest ID"
                  value={item.questId || ''}
                  onChange={(v) => update(i, { questId: v })}
                  tooltip="Quest template ID that must be active for this item to spawn."
                />
              </div>
            )}
          </div>
          {showRotation && (
            <div className="flex items-center gap-2 pl-2">
              <Toggle
                label={showRotation ? 'Random Y Rotation' : 'Random Rotation'}
                checked={item.randomRotation ?? true}
                onChange={(v) => update(i, { randomRotation: v })}
                tooltip={showRotation ? "Randomize the item yaw so it stays upright." : "Randomize the item rotation when it spawns."}
              />
              {!item.randomRotation && (
                <div className="flex-1">
                  <TransformField label="Rotation" value={item.rotation ?? defaultTransform()} onChange={(v) => update(i, { rotation: v })} tooltip="Fixed rotation used when Random Rotation is off." />
                </div>
              )}
            </div>
          )}
        </div>
      ))}
      <button onClick={() => onChange([...value, defaultLootItem()])} className="btn-secondary text-sm flex items-center gap-1">
        <Plus size={14} /> Add Item
      </button>
    </div>
  )
}

function SpawnList({
  data,
  onChange,
}: {
  data: LooseLootSpawn[]
  onChange: (spawns: LooseLootSpawn[]) => void
}) {
  const [form, setForm] = useState<LooseLootSpawn>({
    id: generateId(),
    name: 'loot_spawn',
    position: defaultTransform(),
    rotation: defaultTransform(),
    items: [{ ...defaultLootItem(), template: '544fb45d4bdc2dee738b4568' }],
    spawnChance: 100,
    respawnable: false,
    forced: false,
  })

  const add = () => {
    onChange([...data, { ...form, id: generateId() }])
    setForm({
      id: generateId(),
      name: 'loot_spawn',
      position: defaultTransform(),
      rotation: defaultTransform(),
      items: [{ ...defaultLootItem(), template: '544fb45d4bdc2dee738b4568' }],
      spawnChance: 100,
      respawnable: false,
      forced: false,
    })
  }

  const update = (index: number, updates: Partial<LooseLootSpawn>) => {
    onChange(replaceAt(data, index, { ...data[index], ...updates }))
  }

  return (
    <div className="space-y-4">
      <div className="card space-y-4">
        <h3 className="text-sm font-semibold text-tarkov-accent uppercase tracking-wider">Add Loose Loot Spawn</h3>
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
          <TextField label="Name" value={form.name} onChange={(v) => setForm((f) => ({ ...f, name: v }))} tooltip="Unique name for this spawn." />
          <NumberField
            label="Spawn Chance"
            value={form.spawnChance}
            onChange={(v) => setForm((f) => ({ ...f, spawnChance: v }))}
            min={0}
            max={100}
            tooltip="Percent chance this spawn is rolled (0-100)."
          />
          <div className="flex items-end">
            <Toggle
              label="Respawnable"
              checked={form.respawnable}
              onChange={(v) => setForm((f) => ({ ...f, respawnable: v }))}
              tooltip="Whether this spawn can be looted again during the raid."
            />
          </div>
          <div className="flex items-end">
            <Toggle
              label="Forced"
              checked={form.forced}
              onChange={(v) => setForm((f) => ({ ...f, forced: v }))}
              tooltip="Always spawn these items, typically used for quest items."
            />
          </div>
          <TransformField label="Position" value={form.position} onChange={(v) => setForm((f) => ({ ...f, position: v }))} tooltip="World-space position of this spawn." />
          <TransformField label="Rotation" value={form.rotation} onChange={(v) => setForm((f) => ({ ...f, rotation: v }))} tooltip="World-space rotation of this spawn." />
          <div className="md:col-span-2 lg:col-span-4">
            <ItemListEditor value={form.items} onChange={(v) => setForm((f) => ({ ...f, items: v }))} />
          </div>
          <div className="flex items-end md:col-span-2 lg:col-span-2">
            <button onClick={add} className="btn-primary w-full flex items-center justify-center gap-2">
              <Plus size={16} /> Add Loot Spawn
            </button>
          </div>
          <div className="flex items-end md:col-span-2 lg:col-span-2">
            <button
              onClick={async () => {
                try {
                  const text = await navigator.clipboard.readText()
                  const parsed = JSON.parse(text)
                  if (parsed.position && (parsed.items || parsed.itemTpls)) {
                    const migratedItems: LootItem[] = parsed.items
                      ? parsed.items
                      : Array.isArray(parsed.itemTpls)
                        ? parsed.itemTpls.map((t: string) => ({ template: t || '', chance: 100 }))
                        : [{ template: parsed.itemTpls || '', chance: 100 }]
                    setForm({
                      id: generateId(),
                      name: parsed.name || 'imported_spawn',
                      position: parsed.position || defaultTransform(),
                      rotation: parsed.rotation || defaultTransform(),
                      items: migratedItems,
                      spawnChance: parsed.spawnChance ?? 100,
                      respawnable: parsed.respawnable ?? false,
                      forced: parsed.forced ?? false,
                    })
                  } else {
                    alert('Clipboard does not contain a valid spawn JSON. Copy a spawn from the in-game editor first.')
                  }
                } catch {
                  alert('Failed to read clipboard. Make sure the in-game editor copied a spawn JSON.')
                }
              }}
              className="btn-secondary w-full flex items-center justify-center gap-2"
            >
              <ClipboardPaste size={16} /> Paste from Game
            </button>
          </div>
        </div>
      </div>

      <div className="space-y-3">
        {data.map((spawn, i) => (
          <div key={spawn.id} className="card">
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
              <TextField label="Name" value={spawn.name} onChange={(v) => update(i, { name: v })} tooltip="Unique name for this spawn." />
              <NumberField
                label="Spawn Chance"
                value={spawn.spawnChance ?? 100}
                onChange={(v) => update(i, { spawnChance: v })}
                min={0}
                max={100}
                tooltip="Percent chance this spawn is rolled (0-100)."
              />
              <div className="flex items-end justify-between md:col-span-1 lg:col-span-2">
                <div className="flex flex-col gap-1">
                  <Toggle
                    label="Respawnable"
                    checked={spawn.respawnable ?? false}
                    onChange={(v) => update(i, { respawnable: v })}
                    tooltip="Whether this spawn can be looted again during the raid."
                  />
                  <Toggle
                    label="Forced"
                    checked={spawn.forced ?? false}
                    onChange={(v) => update(i, { forced: v })}
                    tooltip="Always spawn these items, typically used for quest items."
                  />
                </div>
                <button onClick={() => onChange(removeAt(data, i))} className="btn-danger p-2">
                  <Trash2 size={16} />
                </button>
              </div>
              <TransformField label="Position" value={spawn.position} onChange={(v) => update(i, { position: v })} tooltip="World-space position of this spawn." />
              <TransformField label="Rotation" value={spawn.rotation} onChange={(v) => update(i, { rotation: v })} tooltip="World-space rotation of this spawn." />
              <div className="md:col-span-2 lg:col-span-4">
                <ItemListEditor value={spawn.items} onChange={(v) => update(i, { items: v })} />
              </div>
            </div>
          </div>
        ))}
        {data.length === 0 && <p className="text-tarkov-text-dim text-sm">No loose loot spawns in this map.</p>}
      </div>
    </div>
  )
}

function ZoneList({
  data,
  onChange,
}: {
  data: LootZone[]
  onChange: (zones: LootZone[]) => void
}) {
  const [form, setForm] = useState<LootZone>({
    id: generateId(),
    name: 'loot_zone',
    position: defaultTransform(),
    rotation: defaultTransform(),
    radius: 1,
    scale: { x: 1, y: 1, z: 1 },
    shape: ZoneShape.Sphere,
    items: [{ ...defaultLootItem(), template: '544fb45d4bdc2dee738b4568' }],
    spawnChance: 100,
    forced: false,
  })

  const add = () => {
    onChange([...data, { ...form, id: generateId() }])
    setForm({
      id: generateId(),
      name: 'loot_zone',
      position: defaultTransform(),
      rotation: defaultTransform(),
      radius: 1,
      scale: { x: 1, y: 1, z: 1 },
      shape: ZoneShape.Sphere,
      items: [{ ...defaultLootItem(), template: '544fb45d4bdc2dee738b4568' }],
      spawnChance: 100,
      forced: false,
    })
  }

  const update = (index: number, updates: Partial<LootZone>) => {
    onChange(replaceAt(data, index, { ...data[index], ...updates }))
  }

  return (
    <div className="space-y-4">
      <div className="card space-y-4">
        <h3 className="text-sm font-semibold text-tarkov-accent uppercase tracking-wider">Add Loot Zone</h3>
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
          <TextField label="Name" value={form.name} onChange={(v) => setForm((f) => ({ ...f, name: v }))} tooltip="Unique name for this loot zone." />
          <SelectField
            label="Shape"
            value={form.shape}
            options={[
              { value: ZoneShape.Sphere, label: 'Sphere' },
              { value: ZoneShape.Box, label: 'Box' },
              { value: ZoneShape.Cylinder, label: 'Cylinder' },
              { value: ZoneShape.Capsule, label: 'Capsule' },
            ]}
            onChange={(v) => setForm((f) => ({ ...f, shape: v }))}
            tooltip="Shape of the zone footprint. Items spawn inside this shape."
          />
          <NumberField
            label="Radius"
            value={form.radius}
            onChange={(v) => setForm((f) => ({ ...f, radius: v }))}
            min={0}
            step={0.1}
            tooltip="Base radius of the zone. Scaled by Scale X for sphere/cylinder/capsule."
          />
          <NumberField
            label="Spawn Chance"
            value={form.spawnChance}
            onChange={(v) => setForm((f) => ({ ...f, spawnChance: v }))}
            min={0}
            max={100}
            tooltip="Percent chance this zone is rolled (0-100)."
          />
          <div className="flex items-end">
            <Toggle
              label="Forced"
              checked={form.forced}
              onChange={(v) => setForm((f) => ({ ...f, forced: v }))}
              tooltip="Always spawn these items, typically used for quest items."
            />
          </div>
          <TransformField label="Position" value={form.position} onChange={(v) => setForm((f) => ({ ...f, position: v }))} tooltip="World-space center of the zone. Items spawn at this height." />
          <TransformField label="Scale" value={form.scale} onChange={(v) => setForm((f) => ({ ...f, scale: v }))} tooltip="Box size for Box shape, or radius/height scale for the other shapes." />
          <div className="md:col-span-2 lg:col-span-4">
            <ItemListEditor value={form.items} onChange={(v) => setForm((f) => ({ ...f, items: v }))} showRotation />
          </div>
          <div className="flex items-end md:col-span-1 lg:col-span-2">
            <button onClick={add} className="btn-primary w-full flex items-center justify-center gap-2">
              <Plus size={16} /> Add Loot Zone
            </button>
          </div>
        </div>
      </div>

      <div className="space-y-3">
        {data.map((zone, i) => (
          <div key={zone.id} className="card">
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
              <TextField label="Name" value={zone.name} onChange={(v) => update(i, { name: v })} tooltip="Unique name for this loot zone." />
              <SelectField
                label="Shape"
                value={zone.shape ?? ZoneShape.Sphere}
                options={[
                  { value: ZoneShape.Sphere, label: 'Sphere' },
                  { value: ZoneShape.Box, label: 'Box' },
                  { value: ZoneShape.Cylinder, label: 'Cylinder' },
                  { value: ZoneShape.Capsule, label: 'Capsule' },
                ]}
                onChange={(v) => update(i, { shape: v })}
                tooltip="Shape of the zone footprint. Items spawn inside this shape."
              />
              <NumberField label="Radius" value={zone.radius ?? 1} onChange={(v) => update(i, { radius: v })} min={0} step={0.1} tooltip="Base radius of the zone. Scaled by Scale X for sphere/cylinder/capsule." />
              <NumberField
                label="Spawn Chance"
                value={zone.spawnChance ?? 100}
                onChange={(v) => update(i, { spawnChance: v })}
                min={0}
                max={100}
                tooltip="Percent chance this zone is rolled (0-100)."
              />
              <div className="flex items-end justify-between">
                <div className="flex flex-col gap-1">
                  <Toggle
                    label="Forced"
                    checked={zone.forced ?? false}
                    onChange={(v) => update(i, { forced: v })}
                    tooltip="Always spawn these items, typically used for quest items."
                  />
                </div>
                <button onClick={() => onChange(removeAt(data, i))} className="btn-danger p-2">
                  <Trash2 size={16} />
                </button>
              </div>
              <TransformField label="Position" value={zone.position} onChange={(v) => update(i, { position: v })} tooltip="World-space center of the zone. Items spawn at this height." />
              <TransformField label="Scale" value={zone.scale ?? { x: 1, y: 1, z: 1 }} onChange={(v) => update(i, { scale: v })} tooltip="Box size for Box shape, or radius/height scale for the other shapes." />
              <div className="md:col-span-2 lg:col-span-4">
                <ItemListEditor value={zone.items} onChange={(v) => update(i, { items: v })} showRotation />
              </div>
            </div>
          </div>
        ))}
        {data.length === 0 && <p className="text-tarkov-text-dim text-sm">No loot zones in this map.</p>}
      </div>
    </div>
  )
}

function ObjectList({
  data,
  onChange,
  bundles,
}: {
  data: StaticObject[]
  onChange: (objects: StaticObject[]) => void
  bundles: BundleInfo[] | null
}) {
  const [form, setForm] = useState<StaticObject>({
    id: generateId(),
    name: 'static_object',
    position: defaultTransform(),
    rotation: defaultTransform(),
    scale: { x: 1, y: 1, z: 1 },
    prefabPath: '',
    sourceObjectName: '',
    sourceObjectPosition: defaultTransform(),
  })

  const add = () => {
    onChange([...data, { ...form, id: generateId() }])
    setForm({
      id: generateId(),
      name: 'static_object',
      position: defaultTransform(),
      rotation: defaultTransform(),
      scale: { x: 1, y: 1, z: 1 },
      prefabPath: '',
      sourceObjectName: '',
      sourceObjectPosition: defaultTransform(),
    })
  }

  const update = (index: number, updates: Partial<StaticObject>) => {
    onChange(replaceAt(data, index, { ...data[index], ...updates }))
  }

  return (
    <div className="space-y-4">
      <div className="card space-y-4">
        <h3 className="text-sm font-semibold text-tarkov-accent uppercase tracking-wider">Add Static Object</h3>
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
          <TextField label="Name" value={form.name} onChange={(v) => setForm((f) => ({ ...f, name: v }))} tooltip="Unique name for the static object." />
          <div className="md:col-span-1 lg:col-span-1">
            <BundleSelector
              label="Prefab Path"
              value={form.prefabPath}
              onChange={(v) => setForm((f) => ({ ...f, prefabPath: v }))}
              bundles={bundles}
              tooltip="Path to the Unity prefab asset to spawn."
            />
          </div>
          <TextField
            label="Source Object Name"
            value={form.sourceObjectName || ''}
            onChange={(v) => setForm((f) => ({ ...f, sourceObjectName: v }))}
            tooltip="Fallback: name of an existing vanilla scene object to copy."
          />
          <TransformField
            label="Source Object Position"
            value={form.sourceObjectPosition ?? defaultTransform()}
            onChange={(v) => setForm((f) => ({ ...f, sourceObjectPosition: v }))}
            tooltip="Original position of the source object used to find it."
          />
          <TransformField label="Position" value={form.position} onChange={(v) => setForm((f) => ({ ...f, position: v }))} tooltip="World-space position of the object." />
          <TransformField label="Rotation" value={form.rotation} onChange={(v) => setForm((f) => ({ ...f, rotation: v }))} tooltip="World-space rotation of the object." />
          <TransformField label="Scale" value={form.scale} onChange={(v) => setForm((f) => ({ ...f, scale: v }))} tooltip="World-space scale of the object." />
          <div className="flex items-end md:col-span-1 lg:col-span-3">
            <button onClick={add} className="btn-primary w-full flex items-center justify-center gap-2">
              <Plus size={16} /> Add Static Object
            </button>
          </div>
        </div>
      </div>

      <div className="space-y-3">
        {data.map((obj, i) => (
          <div key={obj.id} className="card">
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
              <TextField label="Name" value={obj.name} onChange={(v) => update(i, { name: v })} tooltip="Unique name for the static object." />
              <div className="md:col-span-1 lg:col-span-1">
                <BundleSelector
                  label="Prefab Path"
                  value={obj.prefabPath || ''}
                  onChange={(v) => update(i, { prefabPath: v })}
                  bundles={bundles}
                  tooltip="Path to the Unity prefab asset to spawn."
                />
              </div>
              <TextField
                label="Source Object Name"
                value={obj.sourceObjectName || ''}
                onChange={(v) => update(i, { sourceObjectName: v })}
                tooltip="Fallback: name of an existing vanilla scene object to copy."
              />
              <TransformField
                label="Source Object Position"
                value={obj.sourceObjectPosition ?? defaultTransform()}
                onChange={(v) => update(i, { sourceObjectPosition: v })}
                tooltip="Original position of the source object used to find it."
              />
              <TransformField label="Position" value={obj.position} onChange={(v) => update(i, { position: v })} tooltip="World-space position of the object." />
              <TransformField label="Rotation" value={obj.rotation} onChange={(v) => update(i, { rotation: v })} tooltip="World-space rotation of the object." />
              <TransformField label="Scale" value={obj.scale} onChange={(v) => update(i, { scale: v })} tooltip="World-space scale of the object." />
              <div className="flex items-end justify-end md:col-span-1 lg:col-span-3">
                <button onClick={() => onChange(removeAt(data, i))} className="btn-danger p-2">
                  <Trash2 size={16} />
                </button>
              </div>
            </div>
          </div>
        ))}
        {data.length === 0 && <p className="text-tarkov-text-dim text-sm">No static objects in this map.</p>}
      </div>
    </div>
  )
}

function InteractiveObjectList({
  data,
  onChange,
}: {
  data: InteractiveObject[]
  onChange: (objects: InteractiveObject[]) => void
}) {
  const [form, setForm] = useState<InteractiveObject>(defaultInteractiveObject())

  const add = () => {
    onChange([...data, { ...form, id: generateId() }])
    setForm(defaultInteractiveObject())
  }

  const update = (index: number, updates: Partial<InteractiveObject>) => {
    onChange(replaceAt(data, index, { ...data[index], ...updates }))
  }

  const isContainer = form.interactiveType === InteractiveObjectType.Container
  const isStationaryWeapon = form.interactiveType === InteractiveObjectType.StationaryWeapon
  const isSwitch = form.interactiveType === InteractiveObjectType.Switch

  return (
    <div className="space-y-4">
      <div className="card space-y-4">
        <h3 className="text-sm font-semibold text-tarkov-accent uppercase tracking-wider">Add Interactive Object</h3>
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
          <TextField label="Name" value={form.name} onChange={(v) => setForm((f) => ({ ...f, name: v }))} tooltip="Unique name for this interactive object." />
          <SelectField
            label="Type"
            value={form.interactiveType}
            options={[
              { value: InteractiveObjectType.Door, label: 'Door' },
              { value: InteractiveObjectType.Container, label: 'Container' },
              { value: InteractiveObjectType.StationaryWeapon, label: 'Stationary Weapon' },
              { value: InteractiveObjectType.Switch, label: 'Switch' },
            ]}
            onChange={(v) => setForm((f) => ({ ...f, interactiveType: v }))}
            tooltip="Kind of interactive object to spawn."
          />
          <NumberField
            label="Spawn Chance"
            value={form.spawnChance}
            onChange={(v) => setForm((f) => ({ ...f, spawnChance: v }))}
            min={0}
            max={100}
            tooltip="Percent chance this object is spawned (0-100)."
          />
          <div className="flex items-end gap-2">
            <Toggle
              label="Quest only"
              checked={form.questOnly ?? false}
              onChange={(v) => setForm((f) => ({ ...f, questOnly: v }))}
              tooltip="Only spawn this object when the specified quest is active."
            />
          </div>
          {(form.questOnly ?? false) && (
            <TextField
              label="Quest ID"
              value={form.questId || ''}
              onChange={(v) => setForm((f) => ({ ...f, questId: v }))}
              tooltip="Quest template ID that must be active for this object to spawn."
            />
          )}
          <TextField
            label="Source Object Name"
            value={form.sourceObjectName || ''}
            onChange={(v) => setForm((f) => ({ ...f, sourceObjectName: v }))}
            tooltip="Name of an existing vanilla scene object to clone."
          />
          <TransformField
            label="Source Object Position"
            value={form.sourceObjectPosition ?? defaultTransform()}
            onChange={(v) => setForm((f) => ({ ...f, sourceObjectPosition: v }))}
            tooltip="Original position of the source object used to find it."
          />
          <TransformField label="Position" value={form.position} onChange={(v) => setForm((f) => ({ ...f, position: v }))} tooltip="World-space position of the object." />
          <TransformField label="Rotation" value={form.rotation} onChange={(v) => setForm((f) => ({ ...f, rotation: v }))} tooltip="World-space rotation of the object." />
          <TransformField label="Scale" value={form.scale} onChange={(v) => setForm((f) => ({ ...f, scale: v }))} tooltip="World-space scale of the object." />
          {form.interactiveType === InteractiveObjectType.Door && (
            <TextField
              label="Key Template"
              value={form.keyId || ''}
              onChange={(v) => setForm((f) => ({ ...f, keyId: v }))}
              tooltip="ID of the key required to unlock this door."
            />
          )}
          {isContainer && (
            <>
              <TextFieldWithButton
                label="Container ID"
                value={form.containerId || ''}
                onChange={(v) => setForm((f) => ({ ...f, containerId: v }))}
                buttonLabel="Generate"
                onButtonClick={() => setForm((f) => ({ ...f, containerId: generateContainerId() }))}
                tooltip="Unique ID for this container. Must match a StaticLoot entry."
                buttonTooltip="Generate a new random container ID"
              />
              <SelectField
                label="Container Template"
                value={form.containerTemplate || ''}
                options={LOOT_CONTAINER_TEMPLATES.map((t) => ({ value: t.id, label: t.name }))}
                onChange={(v) => setForm((f) => ({ ...f, containerTemplate: v }))}
                tooltip="Root container template to use."
              />
              <SelectField
                label="Loot Mode"
                value={form.lootMode ?? ContainerLootMode.Default}
                options={[
                  { value: ContainerLootMode.Default, label: 'Default (external loot)' },
                  { value: ContainerLootMode.Hybrid, label: 'Hybrid (external + custom)' },
                  { value: ContainerLootMode.Custom, label: 'Custom only' },
                ]}
                onChange={(v) => setForm((f) => ({ ...f, lootMode: v }))}
                tooltip="How this container receives loot."
              />
              <NumberField label="Item Count Min" value={form.itemCountMin ?? 0} onChange={(v) => setForm((f) => ({ ...f, itemCountMin: v }))} min={0} step={1} tooltip="Minimum number of items in the container." />
              <NumberField label="Item Count Max" value={form.itemCountMax ?? 0} onChange={(v) => setForm((f) => ({ ...f, itemCountMax: v }))} min={0} step={1} tooltip="Maximum number of items in the container." />
            </>
          )}
          {isContainer && (
            <div className="md:col-span-2 lg:col-span-4">
              <InteractiveItemListEditor
                value={form.items}
                onChange={(v) => setForm((f) => ({ ...f, items: v }))}
                tooltip="Items injected into the container in Hybrid or Custom mode."
              />
            </div>
          )}
          {isStationaryWeapon && (
            <TextField
              label="Weapon Template"
              value={form.weaponTemplate || ''}
              onChange={(v) => setForm((f) => ({ ...f, weaponTemplate: v }))}
              tooltip="Root template ID of the stationary weapon (e.g. NSV Utes)."
            />
          )}
          {isSwitch && (
            <>
              <Toggle label="Start On" checked={form.switchInitialState ?? false} onChange={(v) => setForm((f) => ({ ...f, switchInitialState: v }))} />
              <TextField
                label="Linked Light Zones"
                value={(form.linkedLightZoneNames ?? []).join(', ')}
                onChange={(v) => setForm((f) => ({ ...f, linkedLightZoneNames: v.split(',').map((s) => s.trim()).filter(Boolean) }))}
                tooltip="Comma-separated light zone names toggled by this switch."
              />
              <TextField
                label="Linked Extracts"
                value={(form.linkedExtractNames ?? []).join(', ')}
                onChange={(v) => setForm((f) => ({ ...f, linkedExtractNames: v.split(',').map((s) => s.trim()).filter(Boolean) }))}
                tooltip="Comma-separated extract zone names toggled by this switch."
              />
            </>
          )}
          <div className="flex items-end md:col-span-1 lg:col-span-3">
            <button onClick={add} className="btn-primary w-full flex items-center justify-center gap-2">
              <Plus size={16} /> Add Interactive Object
            </button>
          </div>
        </div>
      </div>

      <div className="space-y-3">
        {data.map((obj, i) => (
          <div key={obj.id} className="card">
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
              <TextField label="Name" value={obj.name} onChange={(v) => update(i, { name: v })} tooltip="Unique name for this interactive object." />
              <SelectField
                label="Type"
                value={obj.interactiveType}
                options={[
                  { value: InteractiveObjectType.Door, label: 'Door' },
                  { value: InteractiveObjectType.Container, label: 'Container' },
                ]}
                onChange={(v) => update(i, { interactiveType: v })}
                tooltip="Kind of interactive object to spawn."
              />
              <NumberField
                label="Spawn Chance"
                value={obj.spawnChance ?? 100}
                onChange={(v) => update(i, { spawnChance: v })}
                min={0}
                max={100}
                tooltip="Percent chance this object is spawned (0-100)."
              />
              <div className="flex items-end gap-2">
                <Toggle
                  label="Quest only"
                  checked={obj.questOnly ?? false}
                  onChange={(v) => update(i, { questOnly: v })}
                  tooltip="Only spawn this object when the specified quest is active."
                />
              </div>
              {(obj.questOnly ?? false) && (
                <TextField
                  label="Quest ID"
                  value={obj.questId || ''}
                  onChange={(v) => update(i, { questId: v })}
                  tooltip="Quest template ID that must be active for this object to spawn."
                />
              )}
              <TextField
                label="Source Object Name"
                value={obj.sourceObjectName || ''}
                onChange={(v) => update(i, { sourceObjectName: v })}
                tooltip="Name of an existing vanilla scene object to clone."
              />
              <TransformField
                label="Source Object Position"
                value={obj.sourceObjectPosition ?? defaultTransform()}
                onChange={(v) => update(i, { sourceObjectPosition: v })}
                tooltip="Original position of the source object used to find it."
              />
              <TransformField label="Position" value={obj.position} onChange={(v) => update(i, { position: v })} tooltip="World-space position of the object." />
              <TransformField label="Rotation" value={obj.rotation} onChange={(v) => update(i, { rotation: v })} tooltip="World-space rotation of the object." />
              <TransformField label="Scale" value={obj.scale} onChange={(v) => update(i, { scale: v })} tooltip="World-space scale of the object." />
              {obj.interactiveType === InteractiveObjectType.Door && (
                <TextField
                  label="Key Template"
                  value={obj.keyId || ''}
                  onChange={(v) => update(i, { keyId: v })}
                  tooltip="ID of the key required to unlock this door."
                />
              )}
              {obj.interactiveType === InteractiveObjectType.Container && (
                <>
                  <TextFieldWithButton
                    label="Container ID"
                    value={obj.containerId || ''}
                    onChange={(v) => update(i, { containerId: v })}
                    buttonLabel="Generate"
                    onButtonClick={() => update(i, { containerId: generateContainerId() })}
                    tooltip="Unique ID for this container. Must match a StaticLoot entry."
                    buttonTooltip="Generate a new random container ID"
                  />
                  <SelectField
                    label="Container Template"
                    value={obj.containerTemplate || ''}
                    options={LOOT_CONTAINER_TEMPLATES.map((t) => ({ value: t.id, label: t.name }))}
                    onChange={(v) => update(i, { containerTemplate: v })}
                    tooltip="Root container template to use."
                  />
                  <SelectField
                    label="Loot Mode"
                    value={obj.lootMode ?? ContainerLootMode.Default}
                    options={[
                      { value: ContainerLootMode.Default, label: 'Default (external loot)' },
                      { value: ContainerLootMode.Hybrid, label: 'Hybrid (external + custom)' },
                      { value: ContainerLootMode.Custom, label: 'Custom only' },
                    ]}
                    onChange={(v) => update(i, { lootMode: v })}
                    tooltip="How this container receives loot."
                  />
                  <NumberField label="Item Count Min" value={obj.itemCountMin ?? 0} onChange={(v) => update(i, { itemCountMin: v })} min={0} step={1} tooltip="Minimum number of items in the container." />
                  <NumberField label="Item Count Max" value={obj.itemCountMax ?? 0} onChange={(v) => update(i, { itemCountMax: v })} min={0} step={1} tooltip="Maximum number of items in the container." />
                </>
              )}
              {obj.interactiveType === InteractiveObjectType.Container && (
                <div className="md:col-span-2 lg:col-span-4">
                  <InteractiveItemListEditor
                    value={obj.items}
                    onChange={(v) => update(i, { items: v })}
                    tooltip="Items injected into the container in Hybrid or Custom mode."
                  />
                </div>
              )}
              {obj.interactiveType === InteractiveObjectType.StationaryWeapon && (
                <TextField
                  label="Weapon Template"
                  value={obj.weaponTemplate || ''}
                  onChange={(v) => update(i, { weaponTemplate: v })}
                  tooltip="Root template ID of the stationary weapon (e.g. NSV Utes)."
                />
              )}
              {obj.interactiveType === InteractiveObjectType.Switch && (
                <>
                  <Toggle label="Start On" checked={obj.switchInitialState ?? false} onChange={(v) => update(i, { switchInitialState: v })} />
                  <TextField
                    label="Linked Light Zones"
                    value={(obj.linkedLightZoneNames ?? []).join(', ')}
                    onChange={(v) => update(i, { linkedLightZoneNames: v.split(',').map((s) => s.trim()).filter(Boolean) })}
                    tooltip="Comma-separated light zone names toggled by this switch."
                  />
                  <TextField
                    label="Linked Extracts"
                    value={(obj.linkedExtractNames ?? []).join(', ')}
                    onChange={(v) => update(i, { linkedExtractNames: v.split(',').map((s) => s.trim()).filter(Boolean) })}
                    tooltip="Comma-separated extract zone names toggled by this switch."
                  />
                </>
              )}
              <div className="flex items-end justify-end md:col-span-1 lg:col-span-3">
                <button onClick={() => onChange(removeAt(data, i))} className="btn-danger p-2">
                  <Trash2 size={16} />
                </button>
              </div>
            </div>
          </div>
        ))}
        {data.length === 0 && <p className="text-tarkov-text-dim text-sm">No interactive objects in this map.</p>}
      </div>
    </div>
  )
}

function InteractiveItemListEditor({
  value,
  onChange,
  tooltip,
}: {
  value: InteractiveObjectItem[]
  onChange: (items: InteractiveObjectItem[]) => void
  tooltip?: string
}) {
  const update = (index: number, updates: Partial<InteractiveObjectItem>) => {
    onChange(replaceAt(value, index, { ...value[index], ...updates }))
  }

  return (
    <div className="space-y-2">
      <label className="label flex items-center">
        Items
        {tooltip && <Tooltip text={tooltip} />}
      </label>
      {value.map((item, i) => (
        <div key={i} className="space-y-2">
          <div className="flex items-center gap-2">
            <div className="flex-1">
              <ItemSelector label="" value={item.template} onChange={(v) => update(i, { template: v })} tooltip="Item template ID to inject." />
            </div>
            <div className="w-28">
              <NumberField label="%" value={item.chance} onChange={(v) => update(i, { chance: v })} min={0} max={100} tooltip="Percent chance this item is injected (0-100)." />
            </div>
            <div className="w-20">
              <NumberField label="Count" value={item.count ?? 1} onChange={(v) => update(i, { count: v })} min={0} step={1} tooltip="Stack count for this item." />
            </div>
            <button onClick={() => onChange(removeAt(value, i))} className="btn-danger p-2">
              <Trash2 size={16} />
            </button>
          </div>
          <div className="flex items-center gap-2 pl-2">
            <Toggle
              label="Quest only"
              checked={item.questOnly ?? false}
              onChange={(v) => update(i, { questOnly: v })}
              tooltip="Only inject this item when the specified quest is active."
            />
            {item.questOnly && (
              <div className="flex-1">
                <TextField
                  label="Quest ID"
                  value={item.questId || ''}
                  onChange={(v) => update(i, { questId: v })}
                  tooltip="Quest template ID that must be active for this item to be injected."
                />
              </div>
            )}
          </div>
        </div>
      ))}
      <button onClick={() => onChange([...value, { ...defaultInteractiveObjectItem }])} className="btn-secondary text-sm flex items-center gap-1">
        <Plus size={14} /> Add Item
      </button>
    </div>
  )
}

function TextField({
  label,
  value,
  onChange,
  tooltip,
}: {
  label: string
  value: string
  onChange: (value: string) => void
  tooltip?: string
}) {
  return (
    <div>
      <label className="label flex items-center">
        {label}
        {tooltip && <Tooltip text={tooltip} />}
      </label>
      <input className="input-field" value={value} onChange={(e) => onChange(e.target.value)} />
    </div>
  )
}

function TextFieldWithButton({
  label,
  value,
  onChange,
  buttonLabel,
  onButtonClick,
  tooltip,
  buttonTooltip,
}: {
  label: string
  value: string
  onChange: (value: string) => void
  buttonLabel: string
  onButtonClick: () => void
  tooltip?: string
  buttonTooltip?: string
}) {
  return (
    <div>
      <label className="label flex items-center">
        {label}
        {tooltip && <Tooltip text={tooltip} />}
      </label>
      <div className="flex gap-2">
        <input className="input-field flex-1" value={value} onChange={(e) => onChange(e.target.value)} />
        <button onClick={onButtonClick} className="btn-secondary whitespace-nowrap" title={buttonTooltip}>
          {buttonLabel}
        </button>
      </div>
    </div>
  )
}

function SelectField<T extends string>({
  label,
  value,
  options,
  onChange,
  tooltip,
}: {
  label: string
  value: T
  options: { value: T; label: string }[]
  onChange: (value: T) => void
  tooltip?: string
}) {
  return (
    <div>
      <label className="label flex items-center">
        {label}
        {tooltip && <Tooltip text={tooltip} />}
      </label>
      <select
        className="input-field"
        value={value}
        onChange={(e) => onChange(e.target.value as T)}
      >
        {options.map((opt) => (
          <option key={opt.value} value={opt.value}>
            {opt.label}
          </option>
        ))}
      </select>
    </div>
  )
}

function NumberField({
  label,
  value,
  onChange,
  min,
  max,
  step = 1,
  tooltip,
}: {
  label: string
  value: number
  onChange: (value: number) => void
  min?: number
  max?: number
  step?: number
  tooltip?: string
}) {
  return (
    <div>
      <label className="label flex items-center">
        {label}
        {tooltip && <Tooltip text={tooltip} />}
      </label>
      <input
        className="input-field"
        type="number"
        min={min}
        max={max}
        step={step}
        value={value}
        onChange={(e) => onChange(parseFloat(e.target.value) || 0)}
      />
    </div>
  )
}

function TransformField({
  label,
  value,
  onChange,
  tooltip,
}: {
  label: string
  value: TransformData
  onChange: (value: TransformData) => void
  tooltip?: string
}) {
  const update = (axis: keyof TransformData, val: string) => {
    onChange({ ...value, [axis]: parseFloat(val) || 0 })
  }

  return (
    <div>
      <label className="label flex items-center">
        {label}
        {tooltip && <Tooltip text={tooltip} />}
      </label>
      <div className="grid grid-cols-3 gap-2">
        <input
          className="input-field"
          type="number"
          step="0.1"
          placeholder="X"
          value={value.x}
          onChange={(e) => update('x', e.target.value)}
        />
        <input
          className="input-field"
          type="number"
          step="0.1"
          placeholder="Y"
          value={value.y}
          onChange={(e) => update('y', e.target.value)}
        />
        <input
          className="input-field"
          type="number"
          step="0.1"
          placeholder="Z"
          value={value.z}
          onChange={(e) => update('z', e.target.value)}
        />
      </div>
    </div>
  )
}

function Toggle({ label, checked, onChange, tooltip }: { label: string; checked: boolean; onChange: (value: boolean) => void; tooltip?: string }) {
  return (
    <label className="toggle">
      <span className="text-sm text-tarkov-text-dim mr-2 flex items-center">
        {label}
        {tooltip && <Tooltip text={tooltip} />}
      </span>
      <input type="checkbox" checked={checked} onChange={(e) => onChange(e.target.checked)} />
      <span className="toggle-track">
        <span className="toggle-thumb" />
      </span>
    </label>
  )
}

function replaceAt<T>(arr: T[], index: number, item: T): T[] {
  return [...arr.slice(0, index), item, ...arr.slice(index + 1)]
}

function removeAt<T>(arr: T[], index: number): T[] {
  return [...arr.slice(0, index), ...arr.slice(index + 1)]
}
