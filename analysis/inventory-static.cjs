// Read-only target inspection. Writes remain inside this analysis directory.
'use strict';
const fs = require('node:fs'), path = require('node:path'), crypto = require('node:crypto'), zlib = require('node:zlib'), assert = require('node:assert/strict');
const root = 'C:\\Program Files (x86)\\Internet Download Manager';
const installer = 'C:\\Users\\Flugel\\Downloads\\idman643build18.exe';
const out = __dirname;
const sha = b => crypto.createHash('sha256').update(b).digest('hex');
const save = (name, x) => fs.writeFileSync(path.join(out, name), JSON.stringify(x, null, 2));
const hex = x => '0x' + x.toString(16);
function pe(b) {
  const need = (p, n) => { if (!Number.isSafeInteger(p) || p < 0 || p + n > b.length) throw Error('out-of-bounds file range'); };
  const u16 = p => { need(p, 2); return b.readUInt16LE(p); };
  const u32 = p => { need(p, 4); return b.readUInt32LE(p); };
  const str = p => { need(p, 1); const e = b.indexOf(0, p); if (e < p || e - p > 16384) throw Error('invalid string'); return b.toString('utf8', p, e); };
  if (b.length < 64 || b.toString('ascii', 0, 2) !== 'MZ') return null;
  const p = u32(60); need(p, 24); if (b.toString('ascii', p, p + 4) !== 'PE\0\0') return null;
  const machine = u16(p + 4), count = u16(p + 6), osize = u16(p + 20), op = p + 24, magic = u16(op), wide = magic === 0x20b;
  if (magic !== 0x10b && !wide) throw Error('unknown optional header');
  need(op, osize); if (count > 1024) throw Error('section count limit');
  const sections = [];
  for (let i = 0; i < count; i++) {
    const s = op + osize + i * 40; need(s, 40);
    sections.push({name: b.toString('ascii', s, s + 8).replace(/\0.*$/, ''), virtualSize: u32(s + 8), rva: u32(s + 12), rawSize: u32(s + 16), rawOffset: u32(s + 20), characteristics: hex(u32(s + 36))});
  }
  const headersSize = u32(op + 60);
  const rva = a => {
    if (a < headersSize) { need(a, 1); return a; }
    for (const s of sections) if (a >= s.rva && a - s.rva < s.rawSize) { const f = s.rawOffset + a - s.rva; need(f, 1); return f; }
    throw Error('RVA not backed by file: ' + hex(a));
  };
  const nd = u32(op + (wide ? 108 : 92)), dbase = op + (wide ? 112 : 96);
  const dir = n => n < nd && dbase + 8 * n + 8 <= op + osize ? [u32(dbase + 8 * n), u32(dbase + 8 * n + 4)] : [0, 0];
  const imageBase = wide ? b.readBigUInt64LE(op + 24) : BigInt(u32(op + 28));
  const thunk = a => {
    const items = [], start = rva(a), stride = wide ? 8 : 4, flag = wide ? 1n << 63n : 1n << 31n;
    for (let i = 0; i < 65536; i++) { const at = start + i * stride; need(at, stride); const v = wide ? b.readBigUInt64LE(at) : BigInt(u32(at)); if (!v) return items; items.push(v & flag ? {ordinal: Number(v & 0xffffn)} : {name: str(rva(Number(v)) + 2)}); }
    throw Error('thunk count limit');
  };
  const imports = [], delayImports = [], exports = [], resources = [], warnings = [];
  const attempt = (label, fn) => { try { fn(); } catch (e) { warnings.push(label + ': ' + e.message); } };
  attempt('imports', () => {
    const [a, sz] = dir(1); if (!a) return; const start = rva(a);
    for (let i = 0; i < Math.min(Math.floor(sz / 20), 4096); i++) { const s = start + i * 20; const lookup = u32(s), name = u32(s + 12), iat = u32(s + 16); if (!lookup && !name && !iat) break; imports.push({dll: str(rva(name)), symbols: thunk(lookup || iat)}); }
  });
  attempt('delay imports', () => {
    const [a, sz] = dir(13); if (!a) return; const start = rva(a);
    for (let i = 0; i < Math.min(Math.floor(sz / 32), 4096); i++) { const s = start + i * 32, attr = u32(s), name = u32(s + 4), int = u32(s + 16); if (!name) break; const cv = v => attr & 1 ? v : Number(BigInt(v) - imageBase); delayImports.push({dll: str(rva(cv(name))), symbols: thunk(cv(int))}); }
  });
  attempt('exports', () => {
    const [a, sz] = dir(0); if (!a) return; const d = rva(a), base = u32(d + 16), nf = u32(d + 20), nn = u32(d + 24); if (nf > 100000 || nn > 100000) throw Error('export count limit');
    const funcs = rva(u32(d + 28)), names = nn ? rva(u32(d + 32)) : 0, ords = nn ? rva(u32(d + 36)) : 0, byOrdinal = new Map();
    for (let i = 0; i < nn; i++) byOrdinal.set(u16(ords + 2 * i), str(rva(u32(names + 4 * i))));
    for (let i = 0; i < nf; i++) { const target = u32(funcs + 4 * i); if (target) exports.push({ordinal: base + i, name: byOrdinal.get(i) || null, rva: hex(target), forwarder: target >= a && target < a + sz ? str(rva(target)) : null}); }
  });
  attempt('resources', () => {
    const [a] = dir(2); if (!a) return; const start = rva(a), visited = new Set();
    function walk(off, names) {
      if (names.length > 8 || visited.has(off)) throw Error('resource depth/cycle'); visited.add(off);
      const d = start + off, n = u16(d + 12) + u16(d + 14); if (n > 65536) throw Error('resource count limit');
      for (let i = 0; i < n; i++) {
        const s = d + 16 + 8 * i, id = u32(s), dst = u32(s + 4); let name = id;
        if (id & 0x80000000) { const p = start + (id & 0x7fffffff), len = u16(p); need(p + 2, len * 2); name = b.toString('utf16le', p + 2, p + 2 + len * 2); }
        if (dst & 0x80000000) walk(dst & 0x7fffffff, [...names, name]);
        else { const e = start + dst, addr = u32(e), len = u32(e + 4), f = rva(addr); need(f, len); resources.push({path: [...names, name], rva: hex(addr), offset: f, length: len, codePage: u32(e + 8), sha256: sha(b.subarray(f, f + len))}); }
      }
    }
    walk(0, []);
  });
  const [certOffset, certSize] = dir(4), end = Math.max(headersSize, ...sections.map(s => s.rawOffset + s.rawSize));
  const certificates = []; attempt('certificate table', () => { if (!certOffset) return; need(certOffset, certSize); for (let f = certOffset; f < certOffset + certSize;) { const len = u32(f); if (len < 8 || f + len > certOffset + certSize) throw Error('invalid certificate length'); certificates.push({offset: f, length: len, revision: hex(u16(f + 4)), type: u16(f + 6), sha256: sha(b.subarray(f + 8, f + len))}); f += (len + 7) & ~7; } });
  return {machine: hex(machine), architecture: ({332:'x86',34404:'x64',43620:'arm64'})[machine] || 'unknown', format: wide ? 'PE32+' : 'PE32', timestamp: u32(p + 8), characteristics: hex(u16(p + 22)), subsystem: u16(op + 68), imageBase: hex(imageBase), entrypointRva: hex(u32(op + 16)), entrypointFileOffset: (() => {try{return rva(u32(op + 16));}catch{return null;}})(), sections, imports, delayImports, exports, resources, certificateTable: {offset: certOffset, length: certSize, certificates}, overlay: {offset: end, length: Math.max(0, b.length - end), includesCertificateTable: !!certOffset}, warnings};
}
function crc32(b) { let c = 0xffffffff; for (const x of b) { c ^= x; for (let i = 0; i < 8; i++) c = c >>> 1 ^ (c & 1 ? 0xedb88320 : 0); } return (c ^ 0xffffffff) >>> 0; }
function protobuf(b) {
  let p = 0; const fields = [];
  function vint(){let n=0n,s=0n;for(let i=0;i<10;i++){if(p>=b.length)throw Error('protobuf truncated');const c=b[p++];n|=BigInt(c&127)<<s;if(!(c&128)){if(n>BigInt(Number.MAX_SAFE_INTEGER))throw Error('protobuf integer overflow');return Number(n);}s+=7n;}throw Error('protobuf varint limit');}
  while(p<b.length){const key=vint(),wire=key&7,field=Math.floor(key/8);if(!field)throw Error('protobuf field zero');if(wire===0)fields.push({field,value:vint()});else if(wire===2){const n=vint();if(p+n>b.length)throw Error('protobuf length');fields.push({field,value:b.subarray(p,p+n)});p+=n;}else if(wire===1||wire===5){p+=wire===1?8:4;if(p>b.length)throw Error('protobuf fixed length');}else throw Error('protobuf wire type');}return fields;
}
function zipPackage(file, b) {
  let zipOffset = 0, wrapper = {format:'ZIP'};
  if (b.toString('ascii', 0, 4) === 'Cr24') {
    const version = b.readUInt32LE(4); wrapper = {format:'CRX', version, signatureVerified:false};
    if (version === 2) { const pk = b.readUInt32LE(8), sig = b.readUInt32LE(12); zipOffset = 16 + pk + sig; if (zipOffset > b.length) throw Error('CRX2 size'); const key = b.subarray(16, 16 + pk); wrapper.publicKeySha256 = sha(key); wrapper.derivedExtensionId = sha(key).slice(0, 32).replace(/[0-9a-f]/g, c => String.fromCharCode(97 + parseInt(c, 16))); }
    else if (version === 3) {
      const len = b.readUInt32LE(8); zipOffset = 12 + len; if (zipOffset > b.length) throw Error('CRX3 size'); wrapper.headerLength = len; wrapper.headerSha256 = sha(b.subarray(12, zipOffset));
      const fields=protobuf(b.subarray(12,zipOffset)),signed=fields.find(f=>f.field===10000)?.value;if(!Buffer.isBuffer(signed))throw Error('CRX3 signed header absent');const id=protobuf(signed).find(f=>f.field===1)?.value;if(id?.length===16)wrapper.declaredExtensionId=id.toString('hex').replace(/[0-9a-f]/g,c=>String.fromCharCode(97+parseInt(c,16)));
      const l=Buffer.alloc(4);l.writeUInt32LE(signed.length);const message=Buffer.concat([Buffer.from('CRX3 SignedData\0'),l,signed,b.subarray(zipOffset)]);
      wrapper.proofs=fields.filter(f=>f.field===2||f.field===3).map(f=>{const p=protobuf(f.value),key=p.find(v=>v.field===1)?.value,sig=p.find(v=>v.field===2)?.value;try{return{algorithm:f.field===2?'RSA-SHA256':'ECDSA-SHA256',publicKeySha256:sha(key),matchesDeclaredId:!!id&&sha(key).startsWith(id.toString('hex')),signatureValid:crypto.verify('sha256',message,{key,format:'der',type:'spki'},sig)};}catch(e){return{error:e.message,signatureValid:false};}});
      wrapper.signatureVerified=wrapper.proofs.some(p=>p.signatureValid&&p.matchesDeclaredId);wrapper.trustLimitation='Cryptographic integrity only; no store trust/revocation/installation validation.';
    }
    else throw Error('unsupported CRX version');
  }
  const z = b.subarray(zipOffset); let eocd = -1;
  for (let p = z.length - 22; p >= Math.max(0, z.length - 65557); p--) if (z.readUInt32LE(p) === 0x06054b50 && p + 22 + z.readUInt16LE(p + 20) === z.length) {eocd = p; break;}
  if (eocd < 0) throw Error('ZIP EOCD absent');
  const n = z.readUInt16LE(eocd + 10), cdlen = z.readUInt32LE(eocd + 12), cd = z.readUInt32LE(eocd + 16);
  if (z.readUInt16LE(eocd + 4) || z.readUInt16LE(eocd + 6) || n !== z.readUInt16LE(eocd + 8) || n === 65535 || cd + cdlen > eocd) throw Error('multi-disk/ZIP64/invalid directory unsupported');
  const packageName = file.startsWith(out + path.sep) ? path.relative(path.join(out,'integration-packages'),file).replaceAll(path.sep,'--') : path.basename(file);
  const dest = path.join(out, 'integration-packages', packageName), files = [], seen = new Set(); let p = cd, total = 0;
  for (let i = 0; i < n; i++) {
    if (p + 46 > cd + cdlen || z.readUInt32LE(p) !== 0x02014b50) throw Error('central header');
    const flags = z.readUInt16LE(p + 8), method = z.readUInt16LE(p + 10), crc = z.readUInt32LE(p + 16), compressed = z.readUInt32LE(p + 20), size = z.readUInt32LE(p + 24), nl = z.readUInt16LE(p + 28), xl = z.readUInt16LE(p + 30), cl = z.readUInt16LE(p + 32), attrs = z.readUInt32LE(p + 38), local = z.readUInt32LE(p + 42);
    if (p + 46 + nl + xl + cl > cd + cdlen) throw Error('central entry overflow');
    const name = z.toString('utf8', p + 46, p + 46 + nl); p += 46 + nl + xl + cl;
    if (!name || name.includes('\\') || name.includes(':') || name.startsWith('/') || name.split('/').some(s => s === '..' || s === '.' || /[. ]$/.test(s) || /^(?:con|prn|aux|nul|com[1-9]|lpt[1-9])(?:\.|$)/i.test(s)) || /[\x00-\x1f<>"|?*]/.test(name) || ((attrs >>> 16) & 0xf000) === 0xa000) throw Error('unsafe path/symlink: ' + name);
    if (seen.has(name.toLowerCase())) throw Error('duplicate path'); seen.add(name.toLowerCase());
    if (flags & 1 || ![0,8].includes(method) || size > 128 * 1024 * 1024 || (total += size) > 512 * 1024 * 1024) throw Error('encryption/method/size unsupported');
    const target = path.resolve(dest, name); if (!target.startsWith(dest + path.sep)) throw Error('path escape');
    for(let d=path.dirname(target);d.length>=out.length;d=path.dirname(d)){if(fs.existsSync(d)&&fs.lstatSync(d).isSymbolicLink())throw Error('existing symlink directory');if(d===out)break;}if(fs.existsSync(target)&&fs.lstatSync(target).isSymbolicLink())throw Error('existing symlink target');
    if (local + 30 > z.length || z.readUInt32LE(local) !== 0x04034b50) throw Error('local header');
    if(z.readUInt16LE(local+6)!==flags||z.readUInt16LE(local+8)!==method)throw Error('local metadata mismatch');
    const ln = z.readUInt16LE(local + 26), lx = z.readUInt16LE(local + 28), start = local + 30 + ln + lx;
    if (start + compressed > cd || z.toString('utf8', local + 30, local + 30 + ln) !== name) throw Error('local data/name bounds');
    const raw = z.subarray(start, start + compressed), data = method === 0 ? raw : zlib.inflateRawSync(raw, {maxOutputLength: Math.max(size, 1)});
    if (data.length !== size || crc32(data) !== crc) throw Error('size/CRC mismatch');
    if (name.endsWith('/')) fs.mkdirSync(target, {recursive:true}); else { fs.mkdirSync(path.dirname(target), {recursive:true}); fs.writeFileSync(target, data); }
    files.push({name, size, compressedSize: compressed, method, crc32:hex(crc), sha256:sha(data), extractedRelativePath:path.relative(out, target)});
  }
  if (p !== cd + cdlen) throw Error('central directory length mismatch');
  const manifestFile = path.join(dest, 'manifest.json');
  return {source:file, sha256:sha(b), wrapper, zipOffset, files, manifest:fs.existsSync(manifestFile) ? JSON.parse(fs.readFileSync(manifestFile, 'utf8')) : null};
}
function main() {
  const files = [], binaries = [], packages = [], errors = [];
  function collect(dir) {for (const e of fs.readdirSync(dir, {withFileTypes:true})) { const f = path.join(dir, e.name); if (e.isSymbolicLink()) {errors.push({path:f,error:'symlink not followed'});continue;} if(e.isDirectory()) collect(f); else if(e.isFile()) files.push(f); }}
  collect(root); files.push(installer); files.sort();
  const inventory = files.map(file => {try {const b = fs.readFileSync(file), s = fs.statSync(file); const item = {path:file, relativePath:file === installer ? '[installer]\\' + path.basename(file) : path.relative(root,file), size:b.length, sha256:sha(b), modifiedUtc:s.mtime.toISOString()}; if(b.toString('ascii',0,2)==='MZ') {try{binaries.push({path:file,sha256:item.sha256,...pe(b)});}catch(e){errors.push({path:file,error:'PE: '+e.message});}} if(/\.(crx|xpi)$/i.test(file)) {try{packages.push(zipPackage(file,b));}catch(e){errors.push({path:file,error:'package: '+e.message});}}return item;}catch(e){errors.push({path:file,error:e.message});return {path:file,error:e.message};}});
  const packagedBinaries=[];
  for(let i=0;i<packages.length;i++)for(const f of packages[i].files){const file=path.join(out,f.extractedRelativePath);if(/\.(jar|zip)$/i.test(f.name)){try{packages.push(zipPackage(file,fs.readFileSync(file)));}catch(e){errors.push({path:file,error:'nested package: '+e.message});}}if(/\.(dll|exe|sys)$/i.test(f.name)){try{const b=fs.readFileSync(file);packagedBinaries.push({path:file,container:packages[i].source,sha256:sha(b),...pe(b)});}catch(e){errors.push({path:file,error:'packaged PE: '+e.message});}}}
  save('inventory-files.json',{createdUtc:new Date().toISOString(),scope:[root,installer],hashAlgorithm:'SHA-256',fileCount:inventory.length,totalBytes:inventory.reduce((n,x)=>n+(x.size||0),0),files:inventory,errors});
  save('inventory-packaged-pe.json',{binaries:packagedBinaries,errors});
  save('inventory-pe.json',{parser:'inventory-static.cjs; Node stdlib; no binary executed',binaries,limitations:['Static imports do not prove runtime calls; dynamic GetProcAddress targets may be absent.','CRX3 cryptographic signatures checked without external trust validation; ZIP CRC32 and sizes checked. XPI/JAR signing files inventoried, signatures not validated.','PE resource payloads hashed; version data captured separately by Windows FileVersionInfo.','No installer payload execution or unpacking; overlay may contain packaged payloads.'],errors});
  save('integration-packages.json',{packages,errors:errors.filter(e=>e.error.startsWith('package:'))});
  console.log(JSON.stringify({files:inventory.length,binaries:binaries.length,packages:packages.length,errors},null,2));
}
assert.equal(crc32(Buffer.from('123456789')),0xcbf43926);
assert.deepEqual(protobuf(Buffer.from([10,2,65,66])),[{field:1,value:Buffer.from('AB')}]);
assert.throws(()=>protobuf(Buffer.from([10,5,65])),/protobuf length/);
assert.equal(pe(Buffer.from('not a PE')),null);
assert.throws(()=>pe(Buffer.concat([Buffer.from('MZ'),Buffer.alloc(62,255)])),/out-of-bounds/);
if(require.main===module) main();
