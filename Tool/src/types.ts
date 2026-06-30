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
  sourceObjectName?: string
  sourceObjectPosition?: TransformData
}

export enum InteractiveObjectType {
  Door = 'Door',
  Container = 'Container',
}

export enum ContainerLootMode {
  Default = 'Default',
  Hybrid = 'Hybrid',
  Custom = 'Custom',
}

export interface InteractiveObjectItem {
  template: string
  chance: number
}

export interface InteractiveObject {
  id: string
  name: string
  position: TransformData
  rotation: TransformData
  scale: TransformData
  interactiveType: InteractiveObjectType
  sourceObjectName?: string
  sourceObjectPosition?: TransformData
  keyId?: string
  containerId?: string
  containerTemplate?: string
  lootMode?: ContainerLootMode
  items: InteractiveObjectItem[]
  spawnChance: number
}

export interface MapData {
  map: string
  lootSpawns: LooseLootSpawn[]
  lootZones: LootZone[]
  objects: StaticObject[]
  interactiveObjects: InteractiveObject[]
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

export function generateContainerId(): string {
  return crypto.randomUUID().replace(/-/g, '').slice(0, 24)
}

export const LOOT_CONTAINER_TEMPLATES: { id: string; name: string }[] = [
  { id: '566966cd4bdc2d0c4c8b4578', name: 'Box full of junk' },
  { id: '5d6d2bb386f774785b07a77a', name: 'Buried barrel cache' },
  { id: '578f879c24597735401e6bc6', name: 'Cash register' },
  { id: '5ad74cf586f774391278f6f0', name: 'Cash register TAR2-2' },
  { id: '5d07b91b86f7745a077a9432', name: 'Common fund stash' },
  { id: '5909e4b686f7747f5b744fa4', name: 'Dead Scav' },
  { id: '578f87b7245977356274f2cd', name: 'Drawer' },
  { id: '578f87a3245977356274f2cb', name: 'Duffle bag' },
  { id: '5909d36d86f774660f0bb900', name: 'Grenade box' },
  { id: '5d6d2b5486f774785c2ba8ea', name: 'Ground cache' },
  { id: '578f8778245977358849a9b5', name: 'Jacket' },
  { id: '5914944186f774189e5e76c2', name: 'Jacket 2' },
  { id: '5937ef2b86f77408a47244b3', name: 'Jacket 3' },
  { id: '59387ac686f77401442ddd61', name: 'Jacket 4' },
  { id: '5909d24f86f77466f56e6855', name: 'Medbag SMU06' },
  { id: '5909d4c186f7746ad34e805a', name: 'Medcase' },
  { id: '5d6fe50986f77449d97f7463', name: 'Medical supply crate' },
  { id: '59139c2186f77411564f8e42', name: 'PC block' },
  { id: '5c052cea86f7746ad34e805a', name: 'Plastic suitcase' },
  { id: '5d6fd13186f77424ad2a8c69', name: 'Ration supply crate' },
  { id: '578f8782245977354405a1e3', name: 'Safe' },
  { id: '5d6fd45b86f774317075ed43', name: 'Technical supply crate' },
  { id: '5909d50c86f774659e6aaebe', name: 'Toolbox' },
  { id: '5909d5ef86f77467974efbd8', name: 'Weapon box' },
  { id: '5909d76c86f77471e53d2adf', name: 'Weapon box 2' },
  { id: '5909d7cf86f77470ee57d75a', name: 'Weapon box 3' },
  { id: '5909d89086f77472591234a0', name: 'Weapon box 4' },
  { id: '5909d45286f77465a8136dc6', name: 'Wooden ammo box' },
  { id: '578f87ad245977356274f2cc', name: 'Wooden crate' },
]

export function defaultTransform(): TransformData {
  return { x: 0, y: 0, z: 0 }
}

export function defaultMapData(mapId: string): MapData {
  return {
    map: mapId,
    lootSpawns: [],
    lootZones: [],
    objects: [],
    interactiveObjects: [],
  }
}

export function defaultInteractiveObject(): InteractiveObject {
  return {
    id: generateId(),
    name: 'interactive_object',
    position: defaultTransform(),
    rotation: defaultTransform(),
    scale: { x: 1, y: 1, z: 1 },
    interactiveType: InteractiveObjectType.Container,
    sourceObjectName: '',
    sourceObjectPosition: defaultTransform(),
    keyId: '',
    containerId: '',
    containerTemplate: '578f87a3245977356274f2cb',
    lootMode: ContainerLootMode.Default,
    items: [{ template: '', chance: 100 }],
    spawnChance: 100,
  }
}
