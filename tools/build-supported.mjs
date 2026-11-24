import fs from 'node:fs';
import path from 'node:path';
import {spawnSync} from 'node:child_process';
import crypto from 'node:crypto';
const root=path.resolve(import.meta.dirname,'..');process.chdir(root);
const profiles=JSON.parse(fs.readFileSync('translations/builds.json'));
const input=process.argv[2];if(!input)throw Error('Usage: node tools/build-supported.mjs <game directory or original PCK>');
const full=path.resolve(input),directory=fs.statSync(full).isDirectory()?full:path.dirname(full);
const candidates=new Set([fs.statSync(full).isFile()?full:path.join(full,'Solbrand.pck'),path.join(directory,'Solbrand.pck'),path.join(directory,'.solbrand-zh','Solbrand.pck.original')]);
if(path.basename(directory)==='.solbrand-zh')candidates.add(path.join(path.dirname(directory),'Solbrand.pck'));
const gameDirectory=path.basename(directory)==='.solbrand-zh'?path.dirname(directory):directory;
const archived=path.join(gameDirectory,'.solbrand-zh','archive');
if(fs.existsSync(archived))for(const entry of fs.readdirSync(archived,{withFileTypes:true}))if(entry.isDirectory())candidates.add(path.join(archived,entry.name,'Solbrand.pck.original'));
const found=new Map();
function identify(file){
 if(!fs.existsSync(file))return null;
 const size=fs.statSync(file).size,possible=Object.entries(profiles).filter(([,p])=>p.sourcePackSize===size);if(!possible.length)return null;
 const fd=fs.openSync(file,'r');
 try{
  const head=Buffer.alloc(40);fs.readSync(fd,head,0,40,0);if(head.toString('ascii',0,4)!=='GDPC')return null;
  const base=Number(head.readBigUInt64LE(24)),at=Number(head.readBigUInt64LE(32));if(at<40||at>=size)return null;
  const index=Buffer.alloc(size-at);fs.readSync(fd,index,0,index.length,at);let pos=4;const entries=new Map();
  for(let i=0;i<index.readUInt32LE(0);i++){const n=index.readUInt32LE(pos);pos+=4;const name=index.toString('utf8',pos,pos+n).replace(/\0+$/,'');pos+=n;const offset=Number(index.readBigUInt64LE(pos))+base,length=Number(index.readBigUInt64LE(pos+8));pos+=16;const hash=index.subarray(pos,pos+16).toString('hex');pos+=20;entries.set(name,{offset,length,hash});}
  const project=entries.get('project.binary'),story=entries.get('.godot/imported/Main.ink-e0f455a283e8899f6dee802ddd078da4.res');if(!project||!story)return null;
  const bytes=Buffer.alloc(project.length);fs.readSync(fd,bytes,0,bytes.length,project.offset);const projectHash=crypto.createHash('md5').update(bytes).digest('hex');
  return possible.find(([,p])=>p.sourceProjectMd5===projectHash&&p.sourceStoryMd5===story.hash)?.[0];
 }finally{fs.closeSync(fd);}
}
for(const file of candidates){const id=identify(file);if(id)found.set(id,file);}
if(!found.size)throw Error('No matching ORIGINAL PCK found. Supported Steam builds: '+Object.keys(profiles).join(', '));
for(const[id,file]of found){
 const build=spawnSync(process.execPath,['tools/build.mjs',file],{encoding:'utf8'});
 if(build.status!==0)throw Error(build.stderr||build.stdout||'Patch build failed');
 const output=path.join('dist','builds',id);fs.mkdirSync(output,{recursive:true});
 for(const name of ['manifest.json','solbrand-zh-overlay.pck'])fs.copyFileSync(path.join('dist',name),path.join(output,name));
 console.log('Verified and rebuilt Steam Build '+id);
}
const latest=[...found.keys()].sort((a,b)=>Number(b)-Number(a))[0];
for(const name of ['manifest.json','solbrand-zh-overlay.pck'])fs.copyFileSync(path.join('dist','builds',latest,name),path.join('dist',name));
console.log('Default build: '+latest+'; bundled builds: '+[...found.keys()].join(', '));
