import { useState, type ReactNode } from 'react'
import { Plus, Trash2 } from 'lucide-react'
import {
  type BotSpawnPoint,
  type BotSpawnZone,
  type ExtractZone,
  type ExtractZoneRequirement,
  type LightColorData,
  type LightZone,
  type PmcSpawnZone,
  type TransformData,
  type TriggerZone,
  type WTTQuestZone,
  type WTTStaticObject,
  BotSpawnCategory,
  BotSpawnPreset,
  BotSpawnSide,
  ExtractZoneRequirementType,
  LightType,
  TriggerLightAction,
  TriggerMode,
  TriggerSide,
  ZoneShape,
  defaultBotSpawnPoint,
  defaultBotSpawnZone,
  defaultExtractZone,
  defaultLightColor,
  defaultLightZone,
  defaultPmcSpawnZone,
  defaultTransform,
  defaultTriggerZone,
  defaultWttQuestZone,
  defaultWttStaticObject,
  generateId,
} from './types'

function replaceAt<T>(arr: T[], index: number, value: T): T[] {
  const next = [...arr]
  next[index] = value
  return next
}

function removeAt<T>(arr: T[], index: number): T[] {
  const next = [...arr]
  next.splice(index, 1)
  return next
}

function TextField({
  label,
  value,
  onChange,
  placeholder,
}: {
  label: string
  value: string
  onChange: (v: string) => void
  placeholder?: string
}) {
  return (
    <div>
      <label className="label">{label}</label>
      <input className="input-field" value={value} onChange={(e) => onChange(e.target.value)} placeholder={placeholder} />
    </div>
  )
}

function NumberField({
  label,
  value,
  onChange,
  min,
  max,
  step,
}: {
  label: string
  value: number
  onChange: (v: number) => void
  min?: number
  max?: number
  step?: number
}) {
  return (
    <div>
      <label className="label">{label}</label>
      <input className="input-field" type="number" value={value} onChange={(e) => onChange(parseFloat(e.target.value))} min={min} max={max} step={step ?? 0.1} />
    </div>
  )
}

function TransformField({
  label,
  value,
  onChange,
}: {
  label: string
  value: TransformData
  onChange: (v: TransformData) => void
}) {
  const update = (axis: keyof TransformData, v: number) => {
    onChange({ ...value, [axis]: v })
  }
  return (
    <div>
      <label className="label">{label}</label>
      <div className="grid grid-cols-3 gap-2">
        <input className="input-field" type="number" value={value.x} onChange={(e) => update('x', parseFloat(e.target.value))} placeholder="X" />
        <input className="input-field" type="number" value={value.y} onChange={(e) => update('y', parseFloat(e.target.value))} placeholder="Y" />
        <input className="input-field" type="number" value={value.z} onChange={(e) => update('z', parseFloat(e.target.value))} placeholder="Z" />
      </div>
    </div>
  )
}

function SelectField<T extends string>({
  label,
  value,
  options,
  onChange,
}: {
  label: string
  value: T
  options: { value: T; label: string }[]
  onChange: (v: T) => void
}) {
  return (
    <div>
      <label className="label">{label}</label>
      <select className="input-field" value={value} onChange={(e) => onChange(e.target.value as T)}>
        {options.map((o) => (
          <option key={o.value} value={o.value}>
            {o.label}
          </option>
        ))}
      </select>
    </div>
  )
}

function Toggle({
  label,
  checked,
  onChange,
}: {
  label: string
  checked: boolean
  onChange: (v: boolean) => void
}) {
  return (
    <label className="flex items-center gap-2 text-sm text-tarkov-text cursor-pointer">
      <input type="checkbox" className="accent-tarkov-accent" checked={checked} onChange={(e) => onChange(e.target.checked)} />
      {label}
    </label>
  )
}

function ColorField({
  label,
  value,
  onChange,
}: {
  label: string
  value: LightColorData
  onChange: (v: LightColorData) => void
}) {
  const update = (axis: keyof LightColorData, v: number) => {
    onChange({ ...value, [axis]: v })
  }
  return (
    <div>
      <label className="label">{label}</label>
      <div className="grid grid-cols-4 gap-2">
        <input className="input-field" type="number" value={value.r} onChange={(e) => update('r', parseFloat(e.target.value))} min={0} max={1} step={0.05} placeholder="R" />
        <input className="input-field" type="number" value={value.g} onChange={(e) => update('g', parseFloat(e.target.value))} min={0} max={1} step={0.05} placeholder="G" />
        <input className="input-field" type="number" value={value.b} onChange={(e) => update('b', parseFloat(e.target.value))} min={0} max={1} step={0.05} placeholder="B" />
        <input className="input-field" type="number" value={value.a} onChange={(e) => update('a', parseFloat(e.target.value))} min={0} max={1} step={0.05} placeholder="A" />
      </div>
    </div>
  )
}

function StringArrayField({
  label,
  values,
  onChange,
  placeholder,
}: {
  label: string
  values: string[]
  onChange: (v: string[]) => void
  placeholder?: string
}) {
  return (
    <div>
      <label className="label">{label}</label>
      <div className="space-y-2">
        {values.map((v, i) => (
          <div key={i} className="flex items-center gap-2">
            <input className="input-field" value={v} onChange={(e) => onChange(replaceAt(values, i, e.target.value))} placeholder={placeholder} />
            <button onClick={() => onChange(removeAt(values, i))} className="btn-danger p-2">
              <Trash2 size={14} />
            </button>
          </div>
        ))}
        <button onClick={() => onChange([...values, ''])} className="btn-secondary text-sm flex items-center gap-1">
          <Plus size={14} /> Add
        </button>
      </div>
    </div>
  )
}

function QuestFields({
  item,
  onChange,
}: {
  item: any
  onChange: (u: any) => void
}) {
  return (
    <div className="flex flex-col gap-2">
      <Toggle label="Quest only" checked={item.questOnly ?? false} onChange={(v) => onChange({ questOnly: v })} />
      <Toggle label="Quest completed" checked={item.questCompleted ?? false} onChange={(v) => onChange({ questCompleted: v })} />
      {(item.questOnly || item.questCompleted) && (
        <TextField label="Quest ID" value={item.questId || ''} onChange={(v) => onChange({ questId: v })} />
      )}
    </div>
  )
}

function SimpleMarkerList({
  title,
  items,
  onChange,
  defaultItem,
  renderAddExtra,
  renderItemExtra,
  showSpawnChance,
  showQuest,
}: {
  title: string
  items: any[]
  onChange: (items: any[]) => void
  defaultItem: any
  renderAddExtra?: (item: any, setItem: (u: any) => void) => ReactNode
  renderItemExtra?: (item: any, update: (u: any) => void) => ReactNode
  showSpawnChance?: boolean
  showQuest?: boolean
}) {
  const [form, setForm] = useState<any>(defaultItem)
  const add = () => {
    onChange([...items, { ...form, id: generateId() }])
    setForm(defaultItem)
  }
  const update = (index: number, updates: any) => {
    onChange(replaceAt(items, index, { ...items[index], ...updates }))
  }
  const updateForm = (updates: any) => setForm((f: any) => ({ ...f, ...updates }))

  return (
    <div className="space-y-4">
      <div className="card space-y-4">
        <h3 className="text-sm font-semibold text-tarkov-accent uppercase tracking-wider">Add {title}</h3>
        <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
          <TextField label="Name" value={form.name} onChange={(v) => updateForm({ name: v })} />
          <TransformField label="Position" value={form.position} onChange={(v) => updateForm({ position: v })} />
          <TransformField label="Rotation" value={form.rotation} onChange={(v) => updateForm({ rotation: v })} />
          {showSpawnChance && (
            <NumberField label="Spawn Chance" value={form.spawnChance ?? 100} onChange={(v) => updateForm({ spawnChance: v })} min={0} max={100} />
          )}
          {showQuest && <QuestFields item={form} onChange={updateForm} />}
          {renderAddExtra?.(form, updateForm)}
          <div className="flex items-end md:col-span-2 lg:col-span-4">
            <button onClick={add} className="btn-primary w-full flex items-center justify-center gap-2">
              <Plus size={16} /> Add {title}
            </button>
          </div>
        </div>
      </div>
      <div className="space-y-3">
        {items.map((item, i) => (
          <div key={item.id} className="card">
            <div className="grid grid-cols-1 md:grid-cols-2 lg:grid-cols-4 gap-4">
              <TextField label="Name" value={item.name} onChange={(v) => update(i, { name: v })} />
              <TransformField label="Position" value={item.position} onChange={(v) => update(i, { position: v })} />
              <TransformField label="Rotation" value={item.rotation} onChange={(v) => update(i, { rotation: v })} />
              {showSpawnChance && (
                <NumberField label="Spawn Chance" value={item.spawnChance ?? 100} onChange={(v) => update(i, { spawnChance: v })} min={0} max={100} />
              )}
              {showQuest && <QuestFields item={item} onChange={(u) => update(i, u)} />}
              {renderItemExtra?.(item, (u: any) => update(i, u))}
              <div className="flex items-end justify-end">
                <button onClick={() => onChange(removeAt(items, i))} className="btn-danger p-2">
                  <Trash2 size={16} />
                </button>
              </div>
            </div>
          </div>
        ))}
        {items.length === 0 && <p className="text-tarkov-text-dim text-sm">No {title.toLowerCase()} in this map.</p>}
      </div>
    </div>
  )
}

const zoneShapeOptions = [
  { value: ZoneShape.Sphere, label: 'Sphere' },
  { value: ZoneShape.Box, label: 'Box' },
  { value: ZoneShape.Cylinder, label: 'Cylinder' },
  { value: ZoneShape.Capsule, label: 'Capsule' },
]

const triggerModeOptions = [
  { value: TriggerMode.OneTime, label: 'One Time' },
  { value: TriggerMode.Repeatable, label: 'Repeatable' },
  { value: TriggerMode.OncePerPlayer, label: 'Once Per Player' },
]

const triggerSideOptions = [
  { value: TriggerSide.Any, label: 'Any' },
  { value: TriggerSide.Pmc, label: 'PMC' },
  { value: TriggerSide.Scav, label: 'Scav' },
]

const triggerLightActionOptions = [
  { value: TriggerLightAction.Toggle, label: 'Toggle' },
  { value: TriggerLightAction.Enable, label: 'Enable' },
  { value: TriggerLightAction.Disable, label: 'Disable' },
]

const botSpawnSideOptions = [
  { value: BotSpawnSide.Savage, label: 'Savage' },
  { value: BotSpawnSide.Bear, label: 'BEAR' },
  { value: BotSpawnSide.Usec, label: 'USEC' },
  { value: BotSpawnSide.Pmc, label: 'PMC' },
  { value: BotSpawnSide.All, label: 'All' },
]

const botSpawnCategoryOptions = [
  { value: BotSpawnCategory.Bot, label: 'Bot' },
  { value: BotSpawnCategory.Boss, label: 'Boss' },
  { value: BotSpawnCategory.BotPmc, label: 'Bot PMC' },
  { value: BotSpawnCategory.All, label: 'All' },
]

const botSpawnPresetOptions = [
  { value: BotSpawnPreset.Any, label: 'Any' },
  { value: BotSpawnPreset.Scav, label: 'Scav' },
  { value: BotSpawnPreset.SniperScav, label: 'Sniper Scav' },
  { value: BotSpawnPreset.Raider, label: 'Raider' },
  { value: BotSpawnPreset.Rogue, label: 'Rogue' },
  { value: BotSpawnPreset.PMC, label: 'PMC' },
  { value: BotSpawnPreset.Bear, label: 'BEAR' },
  { value: BotSpawnPreset.Usec, label: 'USEC' },
  { value: BotSpawnPreset.Boss, label: 'Boss' },
  { value: BotSpawnPreset.Killa, label: 'Killa' },
  { value: BotSpawnPreset.Tagilla, label: 'Tagilla' },
  { value: BotSpawnPreset.Gluhar, label: 'Gluhar' },
  { value: BotSpawnPreset.Sanitar, label: 'Sanitar' },
  { value: BotSpawnPreset.Kojaniy, label: 'Kojaniy' },
  { value: BotSpawnPreset.Knight, label: 'Knight' },
  { value: BotSpawnPreset.Zryachiy, label: 'Zryachiy' },
  { value: BotSpawnPreset.Boar, label: 'Boar' },
  { value: BotSpawnPreset.Kolontay, label: 'Kolontay' },
  { value: BotSpawnPreset.Partisan, label: 'Partisan' },
  { value: BotSpawnPreset.Cultist, label: 'Cultist' },
  { value: BotSpawnPreset.Infected, label: 'Infected' },
]

const lightTypeOptions = [
  { value: LightType.Point, label: 'Point' },
  { value: LightType.Spot, label: 'Spot' },
  { value: LightType.Directional, label: 'Directional' },
  { value: LightType.Area, label: 'Area' },
]

const extractRequirementTypeOptions = [
  { value: ExtractZoneRequirementType.None, label: 'None' },
  { value: ExtractZoneRequirementType.TransferItem, label: 'Transfer Item' },
  { value: ExtractZoneRequirementType.HasItem, label: 'Has Item' },
  { value: ExtractZoneRequirementType.WearsItem, label: 'Wears Item' },
  { value: ExtractZoneRequirementType.QuestActive, label: 'Quest Active' },
  { value: ExtractZoneRequirementType.QuestCompleted, label: 'Quest Completed' },
]

function RequirementList({
  values,
  onChange,
}: {
  values: ExtractZoneRequirement[]
  onChange: (v: ExtractZoneRequirement[]) => void
}) {
  return (
    <div className="space-y-2">
      <label className="label">Requirements</label>
      {values.map((req, i) => (
        <div key={i} className="card bg-tarkov-bg/50 space-y-2">
          <div className="grid grid-cols-1 md:grid-cols-4 gap-2">
            <SelectField
              label="Type"
              value={req.type as ExtractZoneRequirementType}
              options={extractRequirementTypeOptions}
              onChange={(v) => onChange(replaceAt(values, i, { ...req, type: v }))}
            />
            <TextField label="Template ID" value={req.templateId || ''} onChange={(v) => onChange(replaceAt(values, i, { ...req, templateId: v }))} />
            <NumberField label="Count" value={req.count ?? 1} onChange={(v) => onChange(replaceAt(values, i, { ...req, count: v }))} min={0} />
            <TextField label="Slot" value={req.requiredSlot || ''} onChange={(v) => onChange(replaceAt(values, i, { ...req, requiredSlot: v }))} />
          </div>
          <TextField label="Tip" value={req.requirementTip || ''} onChange={(v) => onChange(replaceAt(values, i, { ...req, requirementTip: v }))} />
          <button onClick={() => onChange(removeAt(values, i))} className="btn-danger text-sm flex items-center gap-1">
            <Trash2 size={14} /> Remove Requirement
          </button>
        </div>
      ))}
      <button onClick={() => onChange([...values, { type: 'None', templateId: '', count: 1, requiredSlot: '', requirementTip: '' }])} className="btn-secondary text-sm flex items-center gap-1">
        <Plus size={14} /> Add Requirement
      </button>
    </div>
  )
}

export function ExtractZoneList({
  data,
  onChange,
}: {
  data: ExtractZone[]
  onChange: (zones: ExtractZone[]) => void
}) {
  const extraFields = (item: any, update: any) => (
    <>
      <SelectField label="Shape" value={item.shape} options={zoneShapeOptions} onChange={(v) => update({ shape: v })} />
      <NumberField label="Radius" value={item.radius} onChange={(v) => update({ radius: v })} min={0} step={0.1} />
      <TransformField label="Scale" value={item.scale} onChange={(v) => update({ scale: v })} />
      <TextField label="Exit Name" value={item.exitName} onChange={(v) => update({ exitName: v })} />
      <NumberField label="Exfil Time" value={item.exfiltrationTime} onChange={(v) => update({ exfiltrationTime: v })} min={0} step={0.1} />
      <TextField label="Exfil Type" value={item.exfiltrationType} onChange={(v) => update({ exfiltrationType: v })} />
      <TextField label="Side" value={item.side || 'Pmc'} onChange={(v) => update({ side: v })} />
      <TextField label="Passage Requirement" value={item.passageRequirement || 'None'} onChange={(v) => update({ passageRequirement: v })} />
      <TextField label="Requirement Tip" value={item.requirementTip || ''} onChange={(v) => update({ requirementTip: v })} />
      <TextField label="Required Slot" value={item.requiredSlot || 'FirstPrimaryWeapon'} onChange={(v) => update({ requiredSlot: v })} />
      <NumberField label="Count" value={item.count ?? 0} onChange={(v) => update({ count: v })} min={0} step={1} />
      <NumberField label="Players Count" value={item.playersCount ?? 0} onChange={(v) => update({ playersCount: v })} min={0} step={1} />
      <Toggle label="Link Lights" checked={item.linkLights ?? false} onChange={(v) => update({ linkLights: v })} />
      <SelectField label="Light Action" value={item.lightAction ?? TriggerLightAction.Toggle} options={triggerLightActionOptions} onChange={(v) => update({ lightAction: v })} />
      <StringArrayField label="Light Zone Names" values={item.lightZoneNames ?? []} onChange={(v) => update({ lightZoneNames: v })} />
      <div className="md:col-span-2 lg:col-span-4">
        <RequirementList values={item.requirements ?? []} onChange={(v) => update({ requirements: v })} />
      </div>
    </>
  )
  return (
    <SimpleMarkerList
      title="Extract Zone"
      items={data}
      onChange={onChange as any}
      defaultItem={defaultExtractZone()}
      showSpawnChance
      showQuest
      renderAddExtra={extraFields}
      renderItemExtra={extraFields}
    />
  )
}

export function LightZoneList({
  data,
  onChange,
}: {
  data: LightZone[]
  onChange: (zones: LightZone[]) => void
}) {
  const extraFields = (item: any, update: any) => (
    <>
      <ColorField label="Color" value={item.color ?? defaultLightColor()} onChange={(v) => update({ color: v })} />
      <NumberField label="Intensity" value={item.intensity} onChange={(v) => update({ intensity: v })} min={0} step={0.1} />
      <NumberField label="Range" value={item.range} onChange={(v) => update({ range: v })} min={0} step={0.1} />
      <NumberField label="Spot Angle" value={item.spotAngle} onChange={(v) => update({ spotAngle: v })} min={0} max={180} step={1} />
      <SelectField label="Light Type" value={item.lightType ?? LightType.Point} options={lightTypeOptions} onChange={(v) => update({ lightType: v })} />
      <Toggle label="Enabled" checked={item.enabled ?? true} onChange={(v) => update({ enabled: v })} />
      <SelectField
        label="Shadows"
        value={item.shadows ?? 'Soft'}
        options={[
          { value: 'None', label: 'None' },
          { value: 'Hard', label: 'Hard' },
          { value: 'Soft', label: 'Soft' },
        ]}
        onChange={(v) => update({ shadows: v })}
      />
      <NumberField label="Shadow Strength" value={item.shadowStrength ?? 1} onChange={(v) => update({ shadowStrength: v })} min={0} max={1} step={0.05} />
      <NumberField label="Shadow Bias" value={item.shadowBias ?? 0.05} onChange={(v) => update({ shadowBias: v })} step={0.001} />
      <NumberField label="Shadow Normal Bias" value={item.shadowNormalBias ?? 0.4} onChange={(v) => update({ shadowNormalBias: v })} step={0.001} />
    </>
  )
  return (
    <SimpleMarkerList
      title="Light Zone"
      items={data}
      onChange={onChange as any}
      defaultItem={defaultLightZone()}
      showSpawnChance
      showQuest
      renderAddExtra={extraFields}
      renderItemExtra={extraFields}
    />
  )
}

export function BotSpawnPointList({
  data,
  onChange,
}: {
  data: BotSpawnPoint[]
  onChange: (points: BotSpawnPoint[]) => void
}) {
  const extraFields = (item: any, update: any) => (
    <>
      <NumberField label="Radius" value={item.radius} onChange={(v) => update({ radius: v })} min={0} step={0.1} />
      <SelectField label="Side" value={item.side} options={botSpawnSideOptions} onChange={(v) => update({ side: v })} />
      <SelectField label="Category" value={item.category} options={botSpawnCategoryOptions} onChange={(v) => update({ category: v })} />
      <SelectField label="Preset" value={item.preset} options={botSpawnPresetOptions} onChange={(v) => update({ preset: v })} />
      <TextField label="Wild Spawn Type" value={item.wildSpawnType || ''} onChange={(v) => update({ wildSpawnType: v })} />
      <NumberField label="Delay (sec)" value={item.delayToCanSpawnSec ?? 4} onChange={(v) => update({ delayToCanSpawnSec: v })} min={0} step={0.1} />
      <TextField label="Bot Zone Name" value={item.botZoneName || ''} onChange={(v) => update({ botZoneName: v })} />
      <TextField label="Spawn Mode" value={item.spawnMode || ''} onChange={(v) => update({ spawnMode: v })} />
      <Toggle label="Force Player Spawn" checked={item.forcePlayerSpawn ?? false} onChange={(v) => update({ forcePlayerSpawn: v })} />
      <NumberField label="Bot Spawn Chance" value={item.botSpawnChance ?? 100} onChange={(v) => update({ botSpawnChance: v })} min={0} max={100} />
      <Toggle label="Trigger Activated" checked={item.triggerActivated ?? false} onChange={(v) => update({ triggerActivated: v })} />
      <TextField label="Trigger Zone Name" value={item.triggerZoneName || ''} onChange={(v) => update({ triggerZoneName: v })} />
      <StringArrayField label="Random Spawn Types" values={item.randomSpawnTypes ?? []} onChange={(v) => update({ randomSpawnTypes: v })} />
    </>
  )
  return (
    <SimpleMarkerList
      title="Bot Spawn Point"
      items={data}
      onChange={onChange as any}
      defaultItem={defaultBotSpawnPoint()}
      showSpawnChance
      showQuest
      renderAddExtra={extraFields}
      renderItemExtra={extraFields}
    />
  )
}

export function BotSpawnZoneList({
  data,
  onChange,
}: {
  data: BotSpawnZone[]
  onChange: (zones: BotSpawnZone[]) => void
}) {
  const extraFields = (item: any, update: any) => (
    <>
      <NumberField label="Radius" value={item.radius} onChange={(v) => update({ radius: v })} min={0} step={0.1} />
      <SelectField label="Shape" value={item.shape} options={zoneShapeOptions} onChange={(v) => update({ shape: v })} />
      <TransformField label="Scale" value={item.scale} onChange={(v) => update({ scale: v })} />
      <SelectField label="Side" value={item.side} options={botSpawnSideOptions} onChange={(v) => update({ side: v })} />
      <SelectField label="Category" value={item.category} options={botSpawnCategoryOptions} onChange={(v) => update({ category: v })} />
      <SelectField label="Preset" value={item.preset} options={botSpawnPresetOptions} onChange={(v) => update({ preset: v })} />
      <TextField label="Wild Spawn Type" value={item.wildSpawnType || ''} onChange={(v) => update({ wildSpawnType: v })} />
      <NumberField label="Spawn Count" value={item.spawnCount} onChange={(v) => update({ spawnCount: v })} min={0} step={1} />
      <NumberField label="Delay (sec)" value={item.delayToCanSpawnSec ?? 4} onChange={(v) => update({ delayToCanSpawnSec: v })} min={0} step={0.1} />
      <TextField label="Bot Zone Name" value={item.botZoneName || ''} onChange={(v) => update({ botZoneName: v })} />
      <TextField label="Spawn Mode" value={item.spawnMode || ''} onChange={(v) => update({ spawnMode: v })} />
      <NumberField label="Bot Spawn Chance" value={item.botSpawnChance ?? 100} onChange={(v) => update({ botSpawnChance: v })} min={0} max={100} />
      <Toggle label="Trigger Activated" checked={item.triggerActivated ?? false} onChange={(v) => update({ triggerActivated: v })} />
      <TextField label="Trigger Zone Name" value={item.triggerZoneName || ''} onChange={(v) => update({ triggerZoneName: v })} />
      <StringArrayField label="Random Spawn Types" values={item.randomSpawnTypes ?? []} onChange={(v) => update({ randomSpawnTypes: v })} />
    </>
  )
  return (
    <SimpleMarkerList
      title="Bot Spawn Zone"
      items={data}
      onChange={onChange as any}
      defaultItem={defaultBotSpawnZone()}
      showSpawnChance
      showQuest
      renderAddExtra={extraFields}
      renderItemExtra={extraFields}
    />
  )
}

export function PmcSpawnZoneList({
  data,
  onChange,
}: {
  data: PmcSpawnZone[]
  onChange: (zones: PmcSpawnZone[]) => void
}) {
  const extraFields = (item: any, update: any) => (
    <>
      <NumberField label="Radius" value={item.radius} onChange={(v) => update({ radius: v })} min={0} step={0.1} />
      <SelectField label="Shape" value={item.shape} options={zoneShapeOptions} onChange={(v) => update({ shape: v })} />
      <TransformField label="Scale" value={item.scale} onChange={(v) => update({ scale: v })} />
      <SelectField label="Side" value={item.side} options={botSpawnSideOptions} onChange={(v) => update({ side: v })} />
      <SelectField label="Category" value={item.category} options={botSpawnCategoryOptions} onChange={(v) => update({ category: v })} />
      <SelectField label="Preset" value={item.preset} options={botSpawnPresetOptions} onChange={(v) => update({ preset: v })} />
      <TextField label="Wild Spawn Type" value={item.wildSpawnType || ''} onChange={(v) => update({ wildSpawnType: v })} />
      <NumberField label="Min Group Size" value={item.minGroupSize ?? 1} onChange={(v) => update({ minGroupSize: v })} min={0} step={1} />
      <NumberField label="Max Group Size" value={item.maxGroupSize ?? 1} onChange={(v) => update({ maxGroupSize: v })} min={0} step={1} />
      <NumberField label="Spawn Chance" value={item.spawnChance ?? 100} onChange={(v) => update({ spawnChance: v })} min={0} max={100} />
      <NumberField label="Delay (sec)" value={item.delayToCanSpawnSec ?? 4} onChange={(v) => update({ delayToCanSpawnSec: v })} min={0} step={0.1} />
      <TextField label="Bot Zone Name" value={item.botZoneName || ''} onChange={(v) => update({ botZoneName: v })} />
      <Toggle label="Force Player Spawn" checked={item.forcePlayerSpawn ?? false} onChange={(v) => update({ forcePlayerSpawn: v })} />
    </>
  )
  return (
    <SimpleMarkerList
      title="PMC Spawn Zone"
      items={data}
      onChange={onChange as any}
      defaultItem={defaultPmcSpawnZone()}
      showSpawnChance
      showQuest
      renderAddExtra={extraFields}
      renderItemExtra={extraFields}
    />
  )
}

export function WttQuestZoneList({
  data,
  onChange,
}: {
  data: WTTQuestZone[]
  onChange: (zones: WTTQuestZone[]) => void
}) {
  const extraFields = (item: any, update: any) => (
    <>
      <TextField label="Zone ID" value={item.zoneId} onChange={(v) => update({ zoneId: v })} />
      <TextField label="Zone Name" value={item.zoneName} onChange={(v) => update({ zoneName: v })} />
      <TextField label="Zone Location" value={item.zoneLocation} onChange={(v) => update({ zoneLocation: v })} />
      <TextField label="Zone Type" value={item.zoneType} onChange={(v) => update({ zoneType: v })} />
      <TextField label="Flare Type" value={item.flareType || ''} onChange={(v) => update({ flareType: v })} />
      <TransformField label="Scale" value={item.scale} onChange={(v) => update({ scale: v })} />
    </>
  )
  return (
    <SimpleMarkerList
      title="WTT Quest Zone"
      items={data}
      onChange={onChange as any}
      defaultItem={defaultWttQuestZone()}
      renderAddExtra={extraFields}
      renderItemExtra={extraFields}
    />
  )
}

export function WttStaticObjectList({
  data,
  onChange,
}: {
  data: WTTStaticObject[]
  onChange: (objects: WTTStaticObject[]) => void
}) {
  const extraFields = (item: any, update: any) => (
    <>
      <SelectField
        label="Spawn Type"
        value={item.spawnType}
        options={[
          { value: 'bundle', label: 'Bundle' },
          { value: 'clone', label: 'Clone' },
        ]}
        onChange={(v) => update({ spawnType: v })}
      />
      <TextField label="Bundle Name" value={item.bundleName || ''} onChange={(v) => update({ bundleName: v })} />
      <TextField label="Prefab Name" value={item.prefabName || ''} onChange={(v) => update({ prefabName: v })} />
      <TextField label="Source Object Name" value={item.sourceObjectName || ''} onChange={(v) => update({ sourceObjectName: v })} />
      <TransformField label="Source Object Position" value={item.sourceObjectPosition || defaultTransform()} onChange={(v) => update({ sourceObjectPosition: v })} />
      <TextField label="Quest ID" value={item.questId || ''} onChange={(v) => update({ questId: v })} />
      <Toggle label="Quest Must Exist" checked={item.questMustExist ?? true} onChange={(v) => update({ questMustExist: v })} />
      <NumberField label="Required Level" value={item.requiredLevel ?? 0} onChange={(v) => update({ requiredLevel: v })} min={0} />
      <TextField label="Required Faction" value={item.requiredFaction || ''} onChange={(v) => update({ requiredFaction: v })} />
      <TextField label="Required Item In Inventory" value={item.requiredItemInInventory || ''} onChange={(v) => update({ requiredItemInInventory: v })} />
      <TextField label="Required Boss Spawned" value={item.requiredBossSpawned || ''} onChange={(v) => update({ requiredBossSpawned: v })} />
      <TextField label="Linked Quest ID" value={item.linkedQuestId || ''} onChange={(v) => update({ linkedQuestId: v })} />
      <StringArrayField label="Required Quest Statuses" values={item.requiredQuestStatuses ?? []} onChange={(v) => update({ requiredQuestStatuses: v })} />
      <StringArrayField label="Excluded Quest Statuses" values={item.excludedQuestStatuses ?? []} onChange={(v) => update({ excludedQuestStatuses: v })} />
      <StringArrayField label="Linked Required Statuses" values={item.linkedRequiredStatuses ?? []} onChange={(v) => update({ linkedRequiredStatuses: v })} />
      <StringArrayField label="Linked Excluded Statuses" values={item.linkedExcludedStatuses ?? []} onChange={(v) => update({ linkedExcludedStatuses: v })} />
    </>
  )
  return (
    <SimpleMarkerList
      title="WTT Static Object"
      items={data}
      onChange={onChange as any}
      defaultItem={defaultWttStaticObject()}
      showQuest
      renderAddExtra={extraFields}
      renderItemExtra={extraFields}
    />
  )
}

export function TriggerZoneList({
  data,
  onChange,
}: {
  data: TriggerZone[]
  onChange: (zones: TriggerZone[]) => void
}) {
  const extraFields = (item: any, update: any) => (
    <>
      <SelectField label="Shape" value={item.shape} options={zoneShapeOptions} onChange={(v) => update({ shape: v })} />
      <TransformField label="Scale" value={item.scale} onChange={(v) => update({ scale: v })} />
      <SelectField label="Trigger Mode" value={item.triggerMode} options={triggerModeOptions} onChange={(v) => update({ triggerMode: v })} />
      <NumberField label="Trigger Chance" value={item.triggerChance} onChange={(v) => update({ triggerChance: v })} min={0} max={100} />
      <NumberField label="Delay (sec)" value={item.delaySeconds ?? 0} onChange={(v) => update({ delaySeconds: v })} min={0} step={0.1} />
      <NumberField label="Cooldown (sec)" value={item.cooldownSeconds ?? 0} onChange={(v) => update({ cooldownSeconds: v })} min={0} step={0.1} />
      <NumberField label="Min Raid Time" value={item.minRaidTime ?? 0} onChange={(v) => update({ minRaidTime: v })} min={0} step={0.1} />
      <NumberField label="Max Raid Time" value={item.maxRaidTime ?? 0} onChange={(v) => update({ maxRaidTime: v })} min={0} step={0.1} />
      <SelectField label="Allowed Side" value={item.allowedSide} options={triggerSideOptions} onChange={(v) => update({ allowedSide: v })} />
      <SelectField label="Light Action" value={item.lightAction ?? TriggerLightAction.Toggle} options={triggerLightActionOptions} onChange={(v) => update({ lightAction: v })} />
      <StringArrayField label="Light Zone Names" values={item.lightZoneNames ?? []} onChange={(v) => update({ lightZoneNames: v })} />
    </>
  )
  return (
    <SimpleMarkerList
      title="Trigger Zone"
      items={data}
      onChange={onChange as any}
      defaultItem={defaultTriggerZone()}
      renderAddExtra={extraFields}
      renderItemExtra={extraFields}
    />
  )
}
