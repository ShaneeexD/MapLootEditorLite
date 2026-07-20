export interface TransformData {
  x: number
  y: number
  z: number
}

export interface LootItem {
  template: string
  chance: number
  count?: number
  rotation: TransformData
  randomRotation: boolean
  yOffset?: number
  questOnly?: boolean
  questCompleted?: boolean
  questId?: string
}

export function defaultLootItem(): LootItem {
  return {
    template: '',
    chance: 100,
    count: 1,
    rotation: defaultTransform(),
    randomRotation: true,
    yOffset: 0,
    questOnly: false,
    questCompleted: false,
    questId: '',
  }
}

export interface LooseLootSpawn {
  id: string
  name: string
  group?: string
  position: TransformData
  rotation: TransformData
  itemTpls?: string[]
  items: LootItem[]
  spawnChance: number
  respawnable: boolean
  forced: boolean
  useGravity?: boolean
  questOnly?: boolean
  questCompleted?: boolean
  questId?: string
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
  group?: string
  position: TransformData
  rotation: TransformData
  radius: number
  scale: TransformData
  shape: ZoneShape
  itemTpls?: string[]
  items: LootItem[]
  spawnChance: number
  forced: boolean
  useGravity?: boolean
  questOnly?: boolean
  questCompleted?: boolean
  questId?: string
}

export interface StaticObject {
  id: string
  name: string
  group?: string
  position: TransformData
  rotation: TransformData
  scale: TransformData
  prefabPath: string
  sourceObjectName?: string
  sourceObjectPosition?: TransformData
  questOnly?: boolean
  questCompleted?: boolean
  questId?: string
}

export enum InteractiveObjectType {
  Door = 'Door',
  Container = 'Container',
  StationaryWeapon = 'StationaryWeapon',
  Switch = 'Switch',
}

export enum ContainerLootMode {
  Default = 'Default',
  Hybrid = 'Hybrid',
  Custom = 'Custom',
}

export interface InteractiveObjectItem {
  template: string
  chance: number
  count?: number
  questOnly?: boolean
  questCompleted?: boolean
  questId?: string
}

export interface InteractiveObject {
  id: string
  name: string
  group?: string
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
  itemCountMin?: number
  itemCountMax?: number
  items: InteractiveObjectItem[]
  weaponTemplate?: string
  switchInitialState?: boolean
  linkedLightZoneNames?: string[]
  linkedExtractNames?: string[]
  spawnChance: number
  questOnly?: boolean
  questCompleted?: boolean
  questId?: string
}

export enum ExtractZoneRequirementType {
  None = 'None',
  TransferItem = 'TransferItem',
  HasItem = 'HasItem',
  WearsItem = 'WearsItem',
  QuestActive = 'QuestActive',
  QuestCompleted = 'QuestCompleted',
}

export interface ExtractZoneRequirement {
  type: ExtractZoneRequirementType | string
  templateId?: string
  count?: number
  requiredSlot?: string
  requirementTip?: string
}

export enum TriggerLightAction {
  Toggle = 'Toggle',
  Enable = 'Enable',
  Disable = 'Disable',
}

export enum TriggerMode {
  OneTime = 'OneTime',
  Repeatable = 'Repeatable',
  OncePerPlayer = 'OncePerPlayer',
}

export enum TriggerSide {
  Any = 'Any',
  Pmc = 'Pmc',
  Scav = 'Scav',
}

export interface ExtractZone {
  id: string
  name: string
  group?: string
  position: TransformData
  rotation: TransformData
  radius: number
  scale: TransformData
  shape: ZoneShape
  exitName: string
  exfiltrationTime: number
  exfiltrationType: string
  side?: string
  passageRequirement?: string
  requirementTip?: string
  requiredSlot?: string
  count?: number
  playersCount?: number
  spawnChance: number
  questOnly?: boolean
  questCompleted?: boolean
  questId?: string
  requirements: ExtractZoneRequirement[]
  linkLights?: boolean
  lightAction?: TriggerLightAction
  lightZoneNames?: string[]
}

export enum BotSpawnSide {
  Savage = 'Savage',
  Bear = 'Bear',
  Usec = 'Usec',
  Pmc = 'Pmc',
  All = 'All',
}

export enum BotSpawnCategory {
  Bot = 'Bot',
  Boss = 'Boss',
  BotPmc = 'BotPmc',
  All = 'All',
}

export enum BotSpawnPreset {
  Any = 'Any',
  Scav = 'Scav',
  SniperScav = 'SniperScav',
  Raider = 'Raider',
  Rogue = 'Rogue',
  PMC = 'PMC',
  Bear = 'Bear',
  Usec = 'Usec',
  Boss = 'Boss',
  Killa = 'Killa',
  Tagilla = 'Tagilla',
  Gluhar = 'Gluhar',
  Sanitar = 'Sanitar',
  Kojaniy = 'Kojaniy',
  Knight = 'Knight',
  Zryachiy = 'Zryachiy',
  Boar = 'Boar',
  Kolontay = 'Kolontay',
  Partisan = 'Partisan',
  Cultist = 'Cultist',
  Infected = 'Infected',
}

export interface BotSpawnGroup {
  id: string
  spawnCount: number
  preset: BotSpawnPreset
  wildSpawnType?: string
  side: BotSpawnSide
  category: BotSpawnCategory
}

export interface BotSpawnPoint {
  id: string
  name: string
  group?: string
  position: TransformData
  rotation: TransformData
  radius: number
  side: BotSpawnSide
  category: BotSpawnCategory
  preset: BotSpawnPreset
  wildSpawnType?: string
  spawnChance: number
  delayToCanSpawnSec?: number
  botZoneName?: string
  questOnly?: boolean
  questCompleted?: boolean
  questId?: string
  spawnMode?: string
  botSpawnChance?: number
  randomSpawnTypes?: string[]
  triggerActivated?: boolean
  triggerZoneName?: string
  forcePlayerSpawn?: boolean
}

export interface BotSpawnZone {
  id: string
  name: string
  group?: string
  position: TransformData
  rotation: TransformData
  radius: number
  scale: TransformData
  shape: ZoneShape
  side: BotSpawnSide
  category: BotSpawnCategory
  preset: BotSpawnPreset
  wildSpawnType?: string
  spawnCount: number
  spawnChance: number
  delayToCanSpawnSec?: number
  botZoneName?: string
  questOnly?: boolean
  questCompleted?: boolean
  questId?: string
  spawnMode?: string
  botSpawnChance?: number
  randomSpawnTypes?: string[]
  randomGroups?: BotSpawnGroup[]
  triggerActivated?: boolean
  triggerZoneName?: string
}

export interface PmcSpawnZone {
  id: string
  name: string
  group?: string
  position: TransformData
  rotation: TransformData
  radius: number
  scale: TransformData
  shape: ZoneShape
  side: BotSpawnSide
  category: BotSpawnCategory
  preset: BotSpawnPreset
  wildSpawnType?: string
  minGroupSize?: number
  maxGroupSize?: number
  spawnChance: number
  delayToCanSpawnSec?: number
  botZoneName?: string
  questOnly?: boolean
  questCompleted?: boolean
  questId?: string
  forcePlayerSpawn?: boolean
}

export interface TriggerZone {
  id: string
  name: string
  group?: string
  position: TransformData
  rotation: TransformData
  scale: TransformData
  shape: ZoneShape
  triggerMode: TriggerMode
  triggerChance: number
  delaySeconds?: number
  cooldownSeconds?: number
  minRaidTime?: number
  maxRaidTime?: number
  allowedSide: TriggerSide
  lightAction?: TriggerLightAction
  lightZoneNames?: string[]
}

export interface LightColorData {
  r: number
  g: number
  b: number
  a: number
}

export enum LightType {
  Point = 'Point',
  Spot = 'Spot',
  Directional = 'Directional',
  Area = 'Area',
}

export interface LightZone {
  id: string
  name: string
  group?: string
  position: TransformData
  rotation: TransformData
  color: LightColorData
  intensity: number
  range: number
  spotAngle: number
  lightType: LightType | string
  enabled: boolean
  shadows?: string
  shadowStrength?: number
  shadowBias?: number
  shadowNormalBias?: number
  spawnChance: number
  questOnly?: boolean
  questCompleted?: boolean
  questId?: string
}

export interface WTTQuestZone {
  id: string
  name: string
  group?: string
  position: TransformData
  rotation: TransformData
  zoneId: string
  zoneName: string
  zoneLocation: string
  zoneType: string
  flareType?: string
  scale: TransformData
}

export interface WTTStaticObject {
  id: string
  name: string
  group?: string
  position: TransformData
  rotation: TransformData
  scale: TransformData
  spawnType: string
  bundleName?: string
  prefabName?: string
  sourceObjectName?: string
  sourceObjectPosition?: TransformData
  questId?: string
  requiredQuestStatuses?: string[]
  excludedQuestStatuses?: string[]
  questMustExist?: boolean
  linkedQuestId?: string
  linkedRequiredStatuses?: string[]
  linkedExcludedStatuses?: string[]
  linkedQuestMustExist?: boolean | null
  requiredItemInInventory?: string
  requiredLevel?: number
  requiredFaction?: string
  requiredBossSpawned?: string
  questOnly?: boolean
  questCompleted?: boolean
}

export interface MapData {
  map: string
  lootSpawns: LooseLootSpawn[]
  lootZones: LootZone[]
  objects: StaticObject[]
  interactiveObjects: InteractiveObject[]
  wttQuestZones: WTTQuestZone[]
  wttStaticObjects: WTTStaticObject[]
  extractZones: ExtractZone[]
  botSpawnPoints: BotSpawnPoint[]
  botSpawnZones: BotSpawnZone[]
  pmcSpawnZones: PmcSpawnZone[]
  lightZones: LightZone[]
  triggerZones: TriggerZone[]
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

export function defaultLightColor(): LightColorData {
  return { r: 1, g: 1, b: 1, a: 1 }
}

export function defaultMapData(mapId: string): MapData {
  return {
    map: mapId,
    lootSpawns: [],
    lootZones: [],
    objects: [],
    interactiveObjects: [],
    wttQuestZones: [],
    wttStaticObjects: [],
    extractZones: [],
    botSpawnPoints: [],
    botSpawnZones: [],
    pmcSpawnZones: [],
    lightZones: [],
    triggerZones: [],
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
    itemCountMin: 0,
    itemCountMax: 0,
    items: [{ template: '', chance: 100, count: 1, questOnly: false, questCompleted: false, questId: '' }],
    weaponTemplate: '5cdeb229d7f00c000e7ce174',
    switchInitialState: false,
    linkedLightZoneNames: [],
    linkedExtractNames: [],
    spawnChance: 100,
    questOnly: false,
    questCompleted: false,
    questId: '',
  }
}

export function defaultExtractZone(): ExtractZone {
  return {
    id: generateId(),
    name: 'extract_zone',
    position: defaultTransform(),
    rotation: defaultTransform(),
    radius: 1,
    scale: { x: 1, y: 1, z: 1 },
    shape: ZoneShape.Box,
    exitName: '',
    exfiltrationTime: 5,
    exfiltrationType: 'Individual',
    side: 'Pmc',
    passageRequirement: 'None',
    requirementTip: '',
    requiredSlot: 'FirstPrimaryWeapon',
    count: 0,
    playersCount: 0,
    spawnChance: 100,
    requirements: [],
    linkLights: false,
    lightAction: TriggerLightAction.Toggle,
    lightZoneNames: [],
    questOnly: false,
    questCompleted: false,
    questId: '',
  }
}

export function defaultLightZone(): LightZone {
  return {
    id: generateId(),
    name: 'light_zone',
    position: defaultTransform(),
    rotation: defaultTransform(),
    color: defaultLightColor(),
    intensity: 1,
    range: 10,
    spotAngle: 30,
    lightType: LightType.Point,
    enabled: true,
    shadows: 'Soft',
    shadowStrength: 1,
    shadowBias: 0.05,
    shadowNormalBias: 0.4,
    spawnChance: 100,
    questOnly: false,
    questCompleted: false,
    questId: '',
  }
}

export function defaultBotSpawnPoint(): BotSpawnPoint {
  return {
    id: generateId(),
    name: 'bot_spawn_point',
    position: defaultTransform(),
    rotation: defaultTransform(),
    radius: 1,
    side: BotSpawnSide.Savage,
    category: BotSpawnCategory.Bot,
    preset: BotSpawnPreset.Scav,
    spawnChance: 100,
    delayToCanSpawnSec: 4,
    botZoneName: '',
    questOnly: false,
    questCompleted: false,
    questId: '',
    spawnMode: 'Forced',
    botSpawnChance: 100,
    randomSpawnTypes: [],
    triggerActivated: false,
    triggerZoneName: '',
    forcePlayerSpawn: false,
  }
}

export function defaultPmcSpawnZone(): PmcSpawnZone {
  return {
    id: generateId(),
    name: 'pmc_spawn_zone',
    position: defaultTransform(),
    rotation: defaultTransform(),
    radius: 5,
    scale: { x: 1, y: 1, z: 1 },
    shape: ZoneShape.Sphere,
    side: BotSpawnSide.Pmc,
    category: BotSpawnCategory.BotPmc,
    preset: BotSpawnPreset.PMC,
    wildSpawnType: 'pmcBot',
    minGroupSize: 1,
    maxGroupSize: 1,
    spawnChance: 100,
    delayToCanSpawnSec: 4,
    botZoneName: '',
    questOnly: false,
    questCompleted: false,
    questId: '',
    forcePlayerSpawn: false,
  }
}

export function defaultBotSpawnZone(): BotSpawnZone {
  return {
    id: generateId(),
    name: 'bot_spawn_zone',
    position: defaultTransform(),
    rotation: defaultTransform(),
    radius: 5,
    scale: { x: 1, y: 1, z: 1 },
    shape: ZoneShape.Sphere,
    side: BotSpawnSide.Savage,
    category: BotSpawnCategory.Bot,
    preset: BotSpawnPreset.Scav,
    spawnCount: 3,
    spawnChance: 100,
    delayToCanSpawnSec: 4,
    botZoneName: '',
    questOnly: false,
    questCompleted: false,
    questId: '',
    spawnMode: 'Forced',
    botSpawnChance: 100,
    randomSpawnTypes: [],
    randomGroups: [],
    triggerActivated: false,
    triggerZoneName: '',
  }
}

export function defaultWttQuestZone(): WTTQuestZone {
  return {
    id: generateId(),
    name: 'wtt_quest_zone',
    position: defaultTransform(),
    rotation: defaultTransform(),
    zoneId: '',
    zoneName: '',
    zoneLocation: '',
    zoneType: 'placeitem',
    flareType: '',
    scale: { x: 1, y: 1, z: 1 },
  }
}

export function defaultWttStaticObject(): WTTStaticObject {
  return {
    id: generateId(),
    name: 'wtt_static_object',
    position: defaultTransform(),
    rotation: defaultTransform(),
    scale: { x: 1, y: 1, z: 1 },
    spawnType: 'bundle',
    bundleName: '',
    prefabName: '',
    sourceObjectName: '',
    sourceObjectPosition: defaultTransform(),
    questId: '',
    requiredQuestStatuses: [],
    excludedQuestStatuses: [],
    questMustExist: true,
    linkedQuestId: '',
    linkedRequiredStatuses: [],
    linkedExcludedStatuses: [],
    linkedQuestMustExist: null,
    requiredItemInInventory: '',
    requiredLevel: 0,
    requiredFaction: '',
    requiredBossSpawned: '',
    questOnly: false,
    questCompleted: false,
  }
}

export function defaultTriggerZone(): TriggerZone {
  return {
    id: generateId(),
    name: 'trigger_zone',
    position: defaultTransform(),
    rotation: defaultTransform(),
    scale: { x: 1, y: 1, z: 1 },
    shape: ZoneShape.Sphere,
    triggerMode: TriggerMode.OneTime,
    triggerChance: 100,
    delaySeconds: 0,
    cooldownSeconds: 0,
    minRaidTime: 0,
    maxRaidTime: 0,
    allowedSide: TriggerSide.Any,
    lightAction: TriggerLightAction.Toggle,
    lightZoneNames: [],
  }
}
