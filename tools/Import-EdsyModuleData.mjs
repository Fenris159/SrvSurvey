// Extract factual catalogue and slot data from a pinned EDSY revision.
// Node.js is needed only to refresh the checked-in snapshot, never at runtime.
import { readFile, writeFile } from 'node:fs/promises';
import { fileURLToPath } from 'node:url';
import { resolve } from 'node:path';
import { createHash } from 'node:crypto';
import vm from 'node:vm';

const revision = 'ea787cf805f2c38f72c8d5f168fa1063341ebd9a';
const root = fileURLToPath(new URL('../', import.meta.url));
const localSource = process.argv[2];
async function source(name) {
  if (localSource) return readFile(resolve(localSource, name), 'utf8');
  const response = await fetch(`https://raw.githubusercontent.com/taleden/EDSY/${revision}/${name}`);
  if (!response.ok) throw new Error(`${name}: HTTP ${response.status}`);
  return response.text();
}
const databaseSource = await source('eddb.js');
const scriptSource = await source('edsy.js');
const digest = source => createHash('sha256').update(source).digest('hex');
const databaseHash = digest(databaseSource);
const scriptHash = digest(scriptSource);
if (databaseHash !== '071f3c63bab630b0db33e7d77061c1dcd7c0d06426c507ab52bf87e9a1c99da2'
    || scriptHash !== '4c314ddb773bc9302169b47074c68daafcec8d2584d024e4853d2ca354086da6') {
  throw new Error(`Source hashes do not match pinned EDSY revision ${revision}`);
}
const context = vm.createContext({ window: {}, document: { title: 'SrvSurvey data import' } });
vm.runInContext(databaseSource, context, { timeout: 5000 });
// EDSY exposes its headless decoder; use it to read factual preset identities.
vm.runInContext(scriptSource, context, { timeout: 5000 });
const database = context.eddb;
const edsy = context.window.edsy;
const presetLiteral = scriptSource.match(/var BUILTIN_STORED_MODULES = (\{[\s\S]*?\n\s*\});/)[1];
const presets = vm.runInContext(`(${presetLiteral})`, context, { timeout: 1000 });
const coreNames = ['Armour', 'PowerPlant', 'MainEngines', 'FrameShiftDrive', 'LifeSupport', 'PowerDistributor', 'Radar', 'FuelTank'];
const sizeNames = ['Tiny', 'Small', 'Medium', 'Large', 'Huge'];
function slotName(ship, group, index, size) {
  const explicit = ship.slotnames?.[group]?.[index];
  if (explicit) return explicit;
  if (group === 'component') return coreNames[index];
  if (group === 'utility') return `TinyHardpoint${index + 1}`;
  if (group === 'hardpoint') {
    const number = ship.slots.hardpoint.slice(0, index + 1).filter(value => value === size).length;
    return `${sizeNames[size]}Hardpoint${number}`;
  }
  const number = String(index + 1).padStart(2, '0');
  return group === 'military' ? `Military${number}` : `Slot${number}_Size${size}`;
}
const ships = Object.values(database.ship).map(ship => ({
  id: Number(ship.id), symbol: ship.fdname, name: ship.name,
  slots: Object.entries(ship.slots).flatMap(([group, sizes]) => sizes.map((size, index) => ({
    name: slotName(ship, group, index, size), group, size,
    types: Object.keys((group === 'component' ? database.group.component[index] : database.group[group]).mtypes),
    restrictedTypes: ship.reserved?.[group]?.[index] ? Object.keys(ship.reserved[group][index]) : [],
  }))),
}));
function moduleRecord(id, module, ship) {
  return {
    id: Number(id), frontierId: module.fdid, symbol: module.fdname,
    name: module.name, type: module.mtype, size: module.class ?? 0,
    rating: module.rating ?? '', mount: module.mount ?? '',
    allowedShips: ship ? [Number(ship.id)] : Object.keys(module.reserved ?? {}).map(Number),
    exactSize: Boolean(module.noundersize), limit: module.limit ?? '',
    mass: Number.isFinite(module.mass) ? module.mass : null,
    powerDraw: Number.isFinite(module.pwrdraw) ? module.pwrdraw : null,
    ...(module.fuelpower ? { jumpDrive: {
      fuelPower: module.fuelpower, fuelMultiplier: module.fuelmul,
      optimalMass: module.fsdoptmass, maximumFuelPerJump: module.maxfuel,
    } } : {}),
    ...(module.jumpbst ? { rangeBoost: module.jumpbst } : {}),
    firegroup: Boolean(database.group.hardpoint.mtypes[module.mtype]
      || (database.group.utility.mtypes[module.mtype] && !['usb', 'upd'].includes(module.mtype))),
  };
}
const modules = Object.entries(database.module).filter(([, module]) => module.fdname)
  .map(([id, module]) => moduleRecord(id, module));
for (const ship of Object.values(database.ship)) {
  for (const [id, overrides] of Object.entries(ship.module ?? {})) {
    const module = { ...database.module[id], ...overrides };
    if (module.mtype === 'cbh' && module.fdname) modules.push(moduleRecord(id, module, ship));
  }
}
const mercenaryModules = Object.entries(presets).filter(([, preset]) => preset.tag === 'M')
  .map(([id, preset]) => {
    const slot = new edsy.Build(1, true).getDetachedSlot();
    if (!slot.setStoredHash(preset.modulehash, null, true)) throw new Error(`Cannot decode MercGear preset ${id}`);
    const module = slot.getModule();
    const blueprintId = slot.getBlueprintID();
    const blueprint = database.blueprint[blueprintId];
    const baseWords = module.name.replace(/\s*\(.*\)$/, '').split(' ');
    const words = blueprint.name.split(' ');
    let overlap = Math.min(words.length, baseWords.length);
    while (overlap > 0 && words.slice(-overlap).join(' ').toLowerCase() !== baseWords.slice(0, overlap).join(' ').toLowerCase()) overlap--;
    return {
      id: Number(id), symbol: module.fdname, name: [...words, ...baseWords.slice(overlap)].join(' '),
      blueprintId, blueprintSymbol: blueprint.fdname, blueprintName: blueprint.name,
      grade: slot.getBlueprintGrade(), effect: database.expeffect[slot.getExpeffectID()]?.fdname ?? null,
      edsyHash: preset.modulehash,
    };
  });
const result = {
  revision, source: `https://github.com/taleden/EDSY/tree/${revision}`,
  sourceDate: database.edsy_lastmodified_db,
  databaseSha256: databaseHash,
  scriptSha256: scriptHash,
  attribution: 'Elite Dangerous game data: Frontier Developments plc. Source: EDSY by taleden; see THIRD-PARTY-NOTICES.md.',
  ships, modules, mercenaryModules, limits: database.limit,
};
const destination = resolve(root, 'src/SrvSurvey.Core/Resources/ReferenceData/edsy-modules.json');
await writeFile(destination, `${JSON.stringify(result, null, 2)}\n`);
console.log(`Wrote ${modules.length} modules, ${ships.length} ships and ${mercenaryModules.length} MercGear presets to ${destination}`);
