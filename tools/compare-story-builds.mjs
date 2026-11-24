import fs from 'node:fs';
import zlib from 'node:zlib';
const directory=process.argv[2];
function story(label){
 const b=fs.readFileSync(`${directory}/${label}-story.res`),count=Math.floor(b.readUInt32LE(12)/b.readUInt32LE(8))+1;
 let p=16+count*4;const blocks=[];
 for(let i=0;i<count;i++){const n=b.readUInt32LE(16+i*4);if(n)blocks.push(zlib.zstdDecompressSync(b.subarray(p,p+n)));p+=n;}
 const raw=Buffer.concat(blocks),at=raw.indexOf(Buffer.from('{"inkVersion"')),length=raw.readUInt32LE(at-4);
 const data=JSON.parse(raw.toString('utf8',at,at+length-1));
 fs.writeFileSync(`${directory}/${label}-story.json`,JSON.stringify(data));return data.root.at(-1);
}
function strings(v,result=[]){if(typeof v==='string'&&v.startsWith('^')){if(!result.includes(v.slice(1)))result.push(v.slice(1));}else if(Array.isArray(v))v.forEach(x=>strings(x,result));else if(v&&typeof v==='object')Object.values(v).forEach(x=>strings(x,result));return result;}
const old=story('old'),current=story('new'),report=[];
for(const [k,v]of Object.entries(current)){
 const a=strings(old[k]),b=strings(v);if(JSON.stringify(a)!==JSON.stringify(b))report.push({knot:k,oldCount:a.length,newCount:b.length,added:b.filter(x=>!a.includes(x)),removed:a.filter(x=>!b.includes(x)),sameIndices:a.every((x,i)=>x===b[i])});
}
fs.writeFileSync(`${directory}/story-diff.json`,JSON.stringify(report,null,2));
console.log(JSON.stringify(report,null,2));
