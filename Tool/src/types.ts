export interface TransformData {
  x: number
  y: number
  z: number
}

export interface LootItem {
  template: string
  chance: number
  rotation: TransformData
  randomRotation: boolean
}

export function defaultLootItem(): LootItem {
  return {
    template: '',
    chance: 100,
    rotation: defaultTransform(),
    randomRotation: true,
  }
}

export interface LooseLootSpawn {
  id: string
  name: string
  position: TransformData
  rotation: TransformData
  items: LootItem[]
  spawnChance: number
  respawnable: boolean
  forced: boolean
}

export enum ZoneShape {
  Sphere = 'Sphere',
  Box = 'Box',
  Cylinder = 'Cylinder',
  Capsule = 'Capsule',
}

export interface LootZone {
  id: string
  name: string
  position: TransformData
  rotation: TransformData
  radius: number
  scale: TransformData
  shape: ZoneShape
  items: LootItem[]
  spawnChance: number
  forced: boolean
}

export interface StaticObject {
  id: string
  name: string
  position: TransformData
  rotation: TransformData
  scale: TransformData
  prefabPath: string
}

export interface MapData {
  map: string
  lootSpawns: LooseLootSpawn[]
  lootZones: LootZone[]
  objects: StaticObject[]
}

export interface PackData {
  name: string
  author: string
  version: string
  maps: Record<string, MapData>
}

export function defaultPackData(): PackData {
  return {
    name: 'My Loot Pack',
    author: '',
    version: '1.0.0',
    maps: {},
  }
}

export const MAP_OPTIONS = [
  { id: 'factory4_day', label: 'Factory (Day)' },
  { id: 'factory4_night', label: 'Factory (Night)' },
  { id: 'customs', label: 'Customs' },
  { id: 'woods', label: 'Woods' },
  { id: 'shoreline', label: 'Shoreline' },
  { id: 'interchange', label: 'Interchange' },
  { id: 'laboratory', label: 'The Lab' },
  { id: 'reserve', label: 'Reserve' },
  { id: 'tarkovstreets', label: 'Streets of Tarkov' },
  { id: 'sandbox', label: 'Ground Zero' },
  { id: 'lighthouse', label: 'Lighthouse' },
  { id: 'hideout', label: 'Hideout' },
]

export function generateId(): string {
  return crypto.randomUUID().replace(/-/g, '').slice(0, 16)
}

export function defaultTransform(): TransformData {
  return { x: 0, y: 0, z: 0 }
}

export function defaultMapData(mapId: string): MapData {
  return {
    map: mapId,
    lootSpawns: [],
    lootZones: [],
    objects: [],
  }
}
