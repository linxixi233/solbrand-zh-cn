import fs from 'node:fs';
import path from 'node:path';
import zlib from 'node:zlib';
import {localizeResource} from './localize-resources.mjs';
import {localizeScene} from './localize-scenes.mjs';
import crypto from 'node:crypto';

const root = path.resolve(import.meta.dirname, '..');
process.chdir(root);
const game = process.argv[2] || 'D:/SteamLibrary/steamapps/common/Solbrand Demo';
const sourcePackPath = fs.statSync(game).isFile() ? game : path.join(game,'Solbrand.pck');
const md5 = b => crypto.createHash('md5').update(b).digest();
const u32 = n => { const b=Buffer.alloc(4); b.writeUInt32LE(n); return b; };
const u64 = n => { const b=Buffer.alloc(8); b.writeBigUInt64LE(BigInt(n)); return b; };
function readPack(filename) {
  const fd=fs.openSync(filename,'r');
  const head=Buffer.alloc(40); fs.readSync(fd,head,0,40,0);
  if(head.toString('ascii',0,4)!=='GDPC'||![3,4].includes(head.readUInt32LE(4))||head.readUInt32LE(20)!==2) throw Error('Unsupported PCK; refusing to patch.');
  const base=Number(head.readBigUInt64LE(24)), directory=Number(head.readBigUInt64LE(32));
  const buf=Buffer.alloc(fs.statSync(filename).size-directory); fs.readSync(fd,buf,0,buf.length,directory);
  let p=4; const entries=new Map();
  for(let i=0;i<buf.readUInt32LE(0);i++) {
    const n=buf.readUInt32LE(p);p+=4; const name=buf.toString('utf8',p,p+n).replace(/\0+$/,'');p+=n;
    const offset=Number(buf.readBigUInt64LE(p))+base;p+=8;const size=Number(buf.readBigUInt64LE(p));p+=8;
    const digest=buf.subarray(p,p+16);p+=16;const flags=buf.readUInt32LE(p);p+=4;
    entries.set(name,{offset,size,digest,flags});
  }
  return {fd,head,base,entries,read(name){const e=entries.get(name);if(!e||e.flags)throw Error('Unsupported entry '+name);const b=Buffer.alloc(e.size);fs.readSync(fd,b,0,b.length,e.offset);if(!md5(b).equals(e.digest))throw Error('Checksum failure '+name);return b;}};
}
function decompress(b) {
  if(b.toString('ascii',0,4)!=='RSCC'||b.readUInt32LE(4)!==2)throw Error('Unsupported compression');
  const block=b.readUInt32LE(8),total=b.readUInt32LE(12),count=Math.floor(total/block)+1;let p=16+count*4,out=[];
  for(let i=0;i<count;i++){const size=b.readUInt32LE(16+i*4);if(size)out.push(zlib.zstdDecompressSync(b.subarray(p,p+size)));p+=size;}
  const raw=Buffer.concat(out);if(raw.length!==total)throw Error('Decompression length mismatch');return raw;
}
function compress(raw) {
  const block=4096,count=Math.floor(raw.length/block)+1,chunks=[];
  for(let i=0;i<count;i++)chunks.push(zlib.zstdCompressSync(raw.subarray(i*block,Math.min((i+1)*block,raw.length))));
  return Buffer.concat([Buffer.from('RSCC'),u32(2),u32(block),u32(raw.length),...chunks.map(b=>u32(b.length)),...chunks,Buffer.from('RSCC')]);
}
function configPatch(buf, additions) {
  if(buf.toString('ascii',0,4)!=='ECFG')throw Error('Unknown project settings format');
  let p=8;const values=new Map();
  for(let i=0;i<buf.readUInt32LE(4);i++){let n=buf.readUInt32LE(p);p+=4;const key=buf.toString('utf8',p,p+n);p+=n;n=buf.readUInt32LE(p);p+=4;values.set(key,buf.subarray(p,p+n));p+=n;}
  for(const [key,value] of Object.entries(additions)) {
    const b=Buffer.from(value);const padding=Buffer.alloc((-b.length)&3);
    values.set(key,Buffer.concat([u32(4),u32(b.length),b,padding]));
  }
  return Buffer.concat([Buffer.from('ECFG'),u32(values.size),...Array.from(values,([k,v])=>{const b=Buffer.from(k);return Buffer.concat([u32(b.length),b,u32(v.length),v]);})]);
}
function writeDirectory(entries,base) {
  return Buffer.concat([u32(entries.size),...Array.from(entries,([n,e])=>{let b=Buffer.from(n);b=Buffer.concat([b,Buffer.alloc((-b.length)&3)]);return Buffer.concat([u32(b.length),b,u64(e.offset-base),u64(e.size),e.digest,u32(e.flags)]);})]);
}
function overlay(filename,replacements) {
  const entries=new Map();let p=128;const parts=[];
  for(const [n,b] of replacements){entries.set(n,{offset:p,size:b.length,digest:md5(b),flags:0});parts.push(b);p+=b.length;const padding=Buffer.alloc((-p)&31);parts.push(padding);p+=padding.length;}
  const head=Buffer.alloc(128);head.write('GDPC');head.writeUInt32LE(3,4);head.writeUInt32LE(4,8);head.writeUInt32LE(7,12);head.writeUInt32LE(2,16);head.writeUInt32LE(2,20);head.writeBigUInt64LE(128n,24);head.writeBigUInt64LE(BigInt(p),32);
  fs.writeFileSync(filename,Buffer.concat([head,...parts,writeDirectory(entries,128)]));
}

fs.mkdirSync('dist',{recursive:true});
const pack=readPack(sourcePackPath);
const project=pack.read('project.binary');
const expectedStory='.godot/imported/Main.ink-e0f455a283e8899f6dee802ddd078da4.res';
const profiles=JSON.parse(fs.readFileSync('translations/builds.json','utf8'));
const supported=Object.entries(profiles).find(([id,p])=>p.sourcePackSize===fs.statSync(sourcePackPath).size&&p.sourceProjectMd5===md5(project).toString('hex')&&pack.entries.get(expectedStory)?.digest.toString('hex')===p.sourceStoryMd5);
if(!supported)throw Error('Unrecognized original game build. Supported builds: '+Object.keys(profiles).join(', ')+'. Refusing to use translations with unverified story indices.');
const [steamBuild,profile]=supported;
const raw=decompress(pack.read(expectedStory));
const at=raw.indexOf(Buffer.from('{"inkVersion"')),oldLength=raw.readUInt32LE(at-4);
if(at<4||raw.subarray(at+oldLength).toString()!=='RSRC')throw Error('Unexpected Ink resource layout');
const story=JSON.parse(raw.toString('utf8',at,at+oldLength-1));
const dictionary=JSON.parse(fs.readFileSync('translations/ui.zh.json','utf8'));
const manualDictionary=JSON.parse(fs.readFileSync('translations/manual.zh.json','utf8'));
Object.assign(dictionary,manualDictionary);
const proseDictionary=fs.existsSync('translations/prose.zh.json')?JSON.parse(fs.readFileSync('translations/prose.zh.json','utf8')):{};
Object.assign(dictionary,proseDictionary);
const expanded=JSON.parse(fs.readFileSync('translations/expanded.zh.json','utf8'));
Object.assign(dictionary,expanded);
const bbcode=s=>[...s.matchAll(/\[(?:\/?[a-zA-Z][^\]]*)\]/g)].map(x=>x[0]).sort();
const readableManual=new Set([
 'Restore and study PERIODICALS burnt b[x]y the sun to gle[/x]an critical information in [x]your search for answers.',
 '[x]THIS IS AN OPTIONAL NOTICE[/x]'
]);
for(const [source,target]of Object.entries(dictionary)){
 if(typeof target!=='string'||!target.length)throw Error('Empty dictionary entry: '+source);
 const comparableSource=readableManual.has(source)?source.replace(/\[\/?x\]/g,''):source;
 if(!['[REDACTED]','[OBSERVATION IS KEY]','[NONE]','[NULL FRAGMENT]'].includes(source)&&JSON.stringify(bbcode(comparableSource))!==JSON.stringify(bbcode(target)))throw Error('Dictionary BBCode changed: '+source);
 const links=s=>[...s.matchAll(/(?:uid|res):\/\/[^\s\[\]"<>]+/g)].map(x=>x[0]).sort();
 if(JSON.stringify(links(source))!==JSON.stringify(links(target)))throw Error('Dictionary resource link changed: '+source);
}
const tutorial=[];
function collect(v){if(typeof v==='string'&&v.startsWith('^')){const t=v.slice(1);if(!tutorial.includes(t))tutorial.push(t);}else if(Array.isArray(v))v.forEach(collect);else if(v&&typeof v==='object')Object.values(v).forEach(collect);}
collect(story.root.at(-1).Tutorial);
const translated=JSON.parse(fs.readFileSync(path.join(profile.storyTranslations,'tutorial.zh.json'),'utf8'));
const tutorialMap=new Map(Object.entries(translated).map(([i,t])=>{if(!tutorial[Number(i)])throw Error('Invalid tutorial index '+i);return [tutorial[Number(i)],t];}));
let translatedTokens=0;
function replace(v){if(typeof v==='string'&&tutorialMap.has(v.slice(1))&&v.startsWith('^')){translatedTokens++;return '^'+tutorialMap.get(v.slice(1));}if(Array.isArray(v))return v.map(replace);if(v&&typeof v==='object')return Object.fromEntries(Object.entries(v).map(([k,x])=>[k,replace(x)]));return v;}
const originalTutorial=structuredClone(story.root.at(-1).Tutorial);
story.root.at(-1).Tutorial=replace(story.root.at(-1).Tutorial);
function verifyStructure(a,b) {
  if(typeof a==='string'){if(a===b)return;if(a.startsWith('^')&&tutorialMap.get(a.slice(1))===b.slice(1)&&b.startsWith('^'))return;throw Error('Unexpected non-text mutation');}
  if(a===null||typeof a!=='object'){if(a!==b)throw Error('Logic mutation');return;}
  if(JSON.stringify(Object.keys(a))!==JSON.stringify(Object.keys(b)))throw Error('Ink container structure changed');
  for(const k of Object.keys(a))verifyStructure(a[k],b[k]);
}
verifyStructure(originalTutorial,story.root.at(-1).Tutorial);
const storyRoot=story.root.at(-1);
const extraCoverage=[];
const storyDirectory=path.join(profile.storyTranslations,'story');
const allStoryTranslations={};
if(fs.existsSync(storyDirectory))for(const file of fs.readdirSync(storyDirectory).filter(n=>n.endsWith('.json')).sort()){
  for(const [knot,entries] of Object.entries(JSON.parse(fs.readFileSync(path.join(storyDirectory,file),'utf8'))))allStoryTranslations[knot]={...allStoryTranslations[knot],...entries};
}
for(const [knot,entries] of Object.entries(allStoryTranslations)){
    if(!Object.hasOwn(storyRoot,knot)||knot==='Tutorial')throw Error('Invalid or duplicate story group: '+knot);
    const sources=[];
    const gather=v=>{if(typeof v==='string'&&v.startsWith('^')){if(!sources.includes(v.slice(1)))sources.push(v.slice(1));}else if(Array.isArray(v))v.forEach(gather);else if(v&&typeof v==='object')Object.values(v).forEach(gather);};
    gather(storyRoot[knot]);
    const tagStrings=new Set(),bodyStrings=new Set();
    const findTags=(v,tag=false)=>{if(Array.isArray(v)){let balance=0;for(const x of v){if(x==='#'){tag=true;balance++;}else if(x==='/#'){tag=false;balance--;}else if(typeof x==='string'&&x.startsWith('^'))(tag?tagStrings:bodyStrings).add(x.slice(1));else if(x&&typeof x==='object')findTags(x,tag);if(balance<0)throw Error('Cross-container Ink tag');}if(balance)throw Error('Cross-container Ink tag');}else if(v&&typeof v==='object')Object.values(v).forEach(x=>findTags(x,tag));};
    findTags(storyRoot[knot]);
    const map=new Map(Object.entries(entries).map(([i,t])=>{
      const s=sources[Number(i)];
      if(s===undefined||typeof t!=='string'||!t.length)throw Error('Invalid translation '+knot+':'+i);
      if(tagStrings.has(s)&&!bodyStrings.has(s)&&s!==t)throw Error('Translation targets a control-only string: '+knot+':'+i);
      if(s.startsWith(': ')&&!t.startsWith(': '))throw Error('Speaker delimiter changed: '+knot+':'+i);
      const tags=s=>[...s.matchAll(/\[(?:\/?[a-zA-Z][^\]]*)\]/g)].map(x=>x[0]).sort();
      if(JSON.stringify(tags(s))!==JSON.stringify(tags(t)))throw Error('BBCode changed: '+knot+':'+i);
      return [s,t];
    }));
    let changes=0;
    const apply=(v,tag=false)=>{if(typeof v==='string'){if(!tag&&v.startsWith('^')&&map.has(v.slice(1))){changes++;return '^'+map.get(v.slice(1));}return v;}if(Array.isArray(v)){let state=tag;return v.map(x=>{if(x==='#'){state=true;return x;}if(x==='/#'){state=false;return x;}return apply(x,state);});}if(v&&typeof v==='object')return Object.fromEntries(Object.entries(v).map(([k,x])=>[k,apply(x,tag)]));return v;};
    const before=storyRoot[knot];const after=apply(before);
    const compare=(a,b)=>{if(typeof a==='string'){if(a!==b&&(!a.startsWith('^')||map.get(a.slice(1))!==b.slice(1)))throw Error('Invalid story mutation');return;}if(a===null||typeof a!=='object'){if(a!==b)throw Error('Story logic changed');return;}if(JSON.stringify(Object.keys(a))!==JSON.stringify(Object.keys(b)))throw Error('Story structure changed');for(const k of Object.keys(a))compare(a[k],b[k]);};
    compare(before,after);
    const tagsOf=(v,result=[])=>{if(Array.isArray(v)){let tag=false;for(const x of v){if(x==='#')tag=true;else if(x==='/#')tag=false;else if(tag)result.push(JSON.stringify(x));else if(x&&typeof x==='object')tagsOf(x,result);}}else if(v&&typeof v==='object')Object.values(v).forEach(x=>tagsOf(x,result));return result;};
    if(JSON.stringify(tagsOf(before))!==JSON.stringify(tagsOf(after)))throw Error('Ink metadata changed: '+knot);
    storyRoot[knot]=after;
    extraCoverage.push({knot,sourceUnique:sources.length,translatedUnique:map.size,replacedTokens:changes});
}
const encoded=Buffer.from(JSON.stringify(story)+'\0');
const patchedRaw=Buffer.concat([raw.subarray(0,at-4),u32(encoded.length),encoded,raw.subarray(at+oldLength)]);
for(const [source,translation] of [...Object.entries(dictionary)]){
  dictionary[source.toUpperCase()]??=translation;
  dictionary[source.toLowerCase()]??=translation;
}
const replacements=new Map([
 ['project.binary',configPatch(project,{'autoload/ZhPatch':'*res://ZhPatch/ZhPatch.gd',
   'application/config/custom_user_dir_name':'Solbrand Demo ZH',
   'application/config/custom_user_dir_name.demo':'Solbrand Demo ZH'})],
 ['ZhPatch/ZhPatch.gd',fs.readFileSync('patch/ZhPatch.gd')],
 ['ZhPatch/zh_CN.json',Buffer.from(JSON.stringify(dictionary))],
 [expectedStory,compress(patchedRaw)]
]);
const proseResources=[];
for(const name of [...pack.entries.keys()].filter(n=>n.startsWith('Data/')&&n.endsWith('.tres.remap')).sort()){
 const target=/path="res:\/\/([^"]+)"/.exec(pack.read(name).toString('utf8'))?.[1];
 if(!target)throw Error('Resource remap missing: '+name);
 const localized=localizeResource(pack.read(target),dictionary);
 if(localized.changes){replacements.set(target,localized.bytes);proseResources.push({resource:name,changedProperties:localized.changes});}
}
const sceneCoverage=[];
for(const name of [...pack.entries.keys()].filter(n=>n.endsWith('.tscn.remap')).sort()){
 const target=/path="res:\/\/([^"]+)"/.exec(pack.read(name).toString('utf8'))?.[1];
 if(!target)throw Error('Scene remap missing: '+name);
 const localized=localizeScene(pack.read(target),dictionary);
 if(localized.changes){replacements.set(target,localized.bytes);sceneCoverage.push({scene:name,changes:localized.changes});}
}
replacements.set('ZhPatch/names.json',fs.readFileSync('translations/names.zh.json'));
replacements.set('ZhPatch/captions.json',fs.readFileSync('translations/captions.zh.json'));
replacements.set('ZhPatch/buttons.json',fs.readFileSync('translations/buttons.zh.json'));
overlay('dist/solbrand-zh-overlay.pck',replacements);
const rebuilt=readPack('dist/solbrand-zh-overlay.pck');
for(const [name,expected]of replacements)if(!rebuilt.read(name).equals(expected))throw Error('Overlay verification failed: '+name);
fs.closeSync(rebuilt.fd);
const manifest={version:'0.4.1',steamBuild,sourcePackSize:fs.statSync(sourcePackPath).size,
 sourceProjectMd5:md5(project).toString('hex'),sourceStoryMd5:pack.entries.get(expectedStory).digest.toString('hex'),
 uiSourceEntries:Object.keys(JSON.parse(fs.readFileSync('translations/ui.zh.json'))).length,manualSourceEntries:Object.keys(manualDictionary).length,proseSourceEntries:Object.keys(proseDictionary).length,uiMessages:Object.keys(dictionary).length,
 tutorialUniqueTranslations:tutorialMap.size,tutorialReplacedTokens:translatedTokens,tutorialSourceUnique: tutorial.length,
 storyGroups:extraCoverage,storyExtraTranslations:extraCoverage.reduce((a,x)=>a+x.translatedUnique,0),
 proseResourceFiles:proseResources.length,proseReplacedProperties:proseResources.reduce((n,x)=>n+x.changedProperties,0),
 localizedScenes:sceneCoverage.length,sceneTextProperties:sceneCoverage.reduce((n,x)=>n+x.changes,0),expandedEntries:Object.keys(expanded).length,
 storyDraftComplete:true,fullStoryTranslation:false,runtimeVerified:false,
 scope:'Chinese first draft of tutorial, main dialogue, side stories, library and author commentaries; interface, manual and 497 prose entries. Control identifiers, foreign-language word clues, development menus and baked texture lettering are preserved. Runtime QA is deferred.'};
fs.writeFileSync('dist/manifest.json',JSON.stringify(manifest,null,2));
fs.closeSync(pack.fd);
console.log(JSON.stringify(manifest,null,2));
