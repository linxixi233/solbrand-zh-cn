import fs from 'node:fs';
import zlib from 'node:zlib';
import path from 'node:path';
export function resource(input, options={}){
 let b=input,p=4;
 const compressed=b.toString('ascii',0,4)==='RSCC';
 if(b.toString('ascii',0,4)==='RSCC'){
  const block=b.readUInt32LE(8),total=b.readUInt32LE(12),count=Math.floor(total/block)+1;let at=16+count*4,out=[];
  for(let i=0;i<count;i++){const size=b.readUInt32LE(16+4*i);if(size)out.push(zlib.zstdDecompressSync(b.subarray(at,at+size)));at+=size;}
  b=Buffer.concat(out);p=0;
 }
 const uint=()=>{const v=b.readUInt32LE(p);p+=4;return v;};
 const str=()=>{const n=uint();const s=b.toString('utf8',p,p+n).replace(/\0+$/,'');p+=n;return s;};
 const qword=()=>{const n=Number(b.readBigUInt64LE(p));p+=8;return n;};
 const real=()=>{const v=b.readFloatLE(p);p+=4;return v;};
 const integer=()=>{const v=b.readInt32LE(p);p+=4;return v;};
 p+=20;const type=str();const importOffsetPosition=p,importOffset=qword();const flags=uint();qword();const scriptClass=flags&8?str():null;p+=44;
 let count=uint();const strings=[];while(count--)strings.push(str());
 count=uint();const externals=[];while(count--){const t=str(),file=str();if(flags&2)qword();externals.push({type:t,file});}
 count=uint();const internals=[],offsets=[],spans=[];while(count--){const name=str(),position=p,offset=qword();internals.push({name,offset});offsets.push({position,offset});}
 const stringId=()=>{const n=uint();if(n&0x80000000){const length=n&0x7fffffff;const s=b.toString('utf8',p,p+length).replace(/\0+$/,'');p+=length;return s;}return strings[n];};
 function variant(trace=''){
  const t=uint();switch(t){
   case 1:return null;
   case 2:return !!uint();
   case 3:{const n=b.readInt32LE(p);p+=4;return n;}
   case 40:return qword();
   case 4:return real();
   case 41:{const n=b.readDoubleLE(p);p+=8;return n;}
   case 5:case 44:{const start=p,value=str();if(options.track)spans.push({key:trace,start,end:p,value});return value;}
   case 22:{const count=b.readUInt16LE(p),sub=b.readUInt16LE(p+2);p+=4;return {nodePath:{names:Array.from({length:count},stringId),subnames:Array.from({length:sub&0x7fff},stringId),absolute:!!(sub&0x8000)}};}
   case 23:return {rid:uint()};
   case 42:case 43:return {unsupportedCallable:t};
   case 10:return [real(),real()];
   case 12:return [real(),real(),real()];
   case 15:case 18:return Array.from({length:6},real);
   case 16:return Array.from({length:9},real);
   case 17:return Array.from({length:12},real);
   case 11:case 13:case 14:case 20:case 50:return [real(),real(),real(),real()];
   case 24:{const kind=uint();if(kind===0)return null;if(kind===2)return {internal:uint()};if(kind===3)return {external:externals[uint()]};throw Error('Object kind '+kind);}
   case 30:{let n=uint()&0x7fffffff;return Array.from({length:n},(_,i)=>variant(trace+'['+i+']'));}
   case 31:{const n=uint(),data=Array.from(b.subarray(p,p+n));p+=n+((-n)&3);return {bytes:data};}
   case 26:{let n=uint()&0x7fffffff,o={};while(n--){const key=variant(trace+'{key}');o[JSON.stringify(key)]=variant(trace+'/'+String(key));}return o;}
   case 32:{const n=uint(),a=[];for(let i=0;i<n;i++){a.push(b.readInt32LE(p));p+=4;}return a;}
   case 33:{const n=uint();return Array.from({length:n},real);}
   case 34:{const n=uint();return Array.from({length:n},str);}
   case 35:{const n=uint();return Array.from({length:n},()=>[real(),real(),real()]);}
   case 37:{const n=uint();return Array.from({length:n},()=>[real(),real()]);}
   case 36:case 53:{const n=uint();return Array.from({length:n},()=>[real(),real(),real(),real()]);}
   case 45:return [integer(),integer()];
   case 46:case 51:return [integer(),integer(),integer(),integer()];
   case 47:return [integer(),integer(),integer()];
   case 52:return Array.from({length:16},real);
   default:throw Error('Unsupported variant '+t+' at '+(p-4));
  }
 }
 for(const entry of internals){p=entry.offset;entry.type=str();const n=uint();entry.properties={};for(let i=0;i<n;i++){const key=strings[uint()];entry.properties[key]=variant(key);}}
 return {type,scriptClass,externals,internals,...(options.track?{binary:{raw:b,compressed,offsets,spans,importOffsetPosition,importOffset}}:{})};
}
if(typeof process !== 'undefined' && process.argv[1] && path.resolve(process.argv[1])===path.resolve(import.meta.dirname,'inspect-settings.mjs')){
process.chdir(path.resolve(import.meta.dirname,'..'));
const index=JSON.parse(fs.readFileSync('analysis/pck-index.json','utf8'));
const result={};
for(const filename of Object.keys(index).filter(n=>/-(vessel_settings_default|georadar_controller_settings_default|game_settings_default|scanner_settings_default|dasher_settings_default|hydrothermal_vent_settings_default|brinicle_settings_default|fishing_scene_settings_default|fish_hook_settings_default|temperature_change_curve|sonar)\.res$/.test(n))){
 try{result[filename]=resource(fs.readFileSync('analysis/extracted/'+filename));}catch(e){result[filename]={error:e.message};}
}
fs.writeFileSync('analysis/settings-evidence.json',JSON.stringify(result,null,2));
for(const [name,res]of Object.entries(result))console.log(JSON.stringify({name:name.split('-').at(-1),data:res.internals?.at(-1)?.properties??res}));
}
