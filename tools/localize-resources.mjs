import zlib from 'node:zlib';
import {resource} from './inspect-settings.mjs';

const u32=n=>{const b=Buffer.alloc(4);b.writeUInt32LE(n);return b;};
export const isProseProperty=key=>/^(?:Title|Description|FenceDescription|Text|StartText|FinishText|FailText|Message|Notes|Label|Name|Origin)$/.test(key);
export function localizeResource(input,dictionary){
 const original=resource(input,{track:true});
 const edits=original.binary.spans.filter(x=>isProseProperty(x.key)&&Object.hasOwn(dictionary,x.value)).map(x=>{
  const text=Buffer.from(dictionary[x.value]+'\0');
  return {...x,bytes:Buffer.concat([u32(text.length),text])};
 }).sort((a,b)=>a.start-b.start);
 const expected={type:original.type,scriptClass:original.scriptClass,externals:original.externals,internals:original.internals.map(x=>({name:x.name,type:x.type,properties:Object.fromEntries(Object.entries(x.properties).map(([k,v])=>[k,typeof v==='string'&&isProseProperty(k)&&Object.hasOwn(dictionary,v)?dictionary[v]:v]))}))};
 return rewriteResource(input,original,edits,expected);
}
export function rewriteResource(input,original,edits,expected){
 const {raw,compressed,offsets,importOffsetPosition,importOffset}=original.binary;
 if(!edits.length)return {bytes:input,changes:0};
 let cursor=0;const parts=[];
 for(const edit of edits){if(edit.start<cursor)throw Error('Overlapping resource strings');parts.push(raw.subarray(cursor,edit.start),edit.bytes);cursor=edit.end;}
 parts.push(raw.subarray(cursor));const translated=Buffer.concat(parts);
 const relocate=position=>position+edits.filter(x=>x.end<=position).reduce((n,x)=>n+x.bytes.length-(x.end-x.start),0);
 for(const {position,offset}of offsets)translated.writeBigUInt64LE(BigInt(relocate(offset)),relocate(position));
 if(importOffset)translated.writeBigUInt64LE(BigInt(relocate(importOffset)),relocate(importOffsetPosition));
 let output=translated;
 if(compressed){
  const block=4096,count=Math.floor(translated.length/block)+1,chunks=[];
  for(let i=0;i<count;i++)chunks.push(zlib.zstdCompressSync(translated.subarray(i*block,Math.min((i+1)*block,translated.length))));
  output=Buffer.concat([Buffer.from('RSCC'),u32(2),u32(block),u32(translated.length),...chunks.map(x=>u32(x.length)),...chunks,Buffer.from('RSCC')]);
 }
 const actual=resource(output);
 const comparable={...actual,internals:actual.internals.map(({offset,...x})=>x)};
 if(JSON.stringify(expected)!==JSON.stringify(comparable))throw Error('Resource round-trip verification failed');
 return {bytes:output,changes:edits.length};
}
