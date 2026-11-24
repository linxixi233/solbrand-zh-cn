"""Read CLR/NativeAOT user strings without loading or executing the assembly."""
import struct, sys, json, re
from pathlib import Path
data=Path(sys.argv[1]).read_bytes()
pe=struct.unpack_from('<I',data,0x3c)[0]
sections=struct.unpack_from('<H',data,pe+6)[0]
optional_size=struct.unpack_from('<H',data,pe+20)[0]
optional=pe+24
magic=struct.unpack_from('<H',data,optional)[0]
directories=optional+(112 if magic==0x20b else 96)
section_start=optional+optional_size
def rva_offset(rva):
    for index in range(sections):
        at=section_start+index*40
        virtual_size,virtual_address,raw_size,raw_offset=struct.unpack_from('<IIII',data,at+8)
        if virtual_address<=rva<virtual_address+max(virtual_size,raw_size):return raw_offset+rva-virtual_address
    raise ValueError('Invalid PE RVA')
cli_rva=struct.unpack_from('<I',data,directories+14*8)[0]
if not cli_rva:
    strings=[]
    for match in re.finditer(rb'(?:[\x20-\x7e]\x00){5,}',data):
        at=match.start()
        if at<4:continue
        n=struct.unpack_from('<I',data,at-4)[0]
        if not 5<=n<=20000 or at+n*2+2>len(data) or data[at+n*2:at+n*2+2]!=b'\0\0':continue
        try:text=data[at:at+n*2].decode('utf-16-le')
        except UnicodeDecodeError:continue
        if any(ord(c)<32 and c not in '\n\r\t' for c in text):continue
        strings.append({'offset':at,'text':text})
    Path(sys.argv[2]).write_text(json.dumps(strings,ensure_ascii=False,indent=2),encoding='utf-8')
    print('Read-only NativeAOT string objects:',len(strings))
    sys.exit(0)
cli=rva_offset(cli_rva)
base=rva_offset(struct.unpack_from('<I',data,cli+8)[0])
if data[base:base+4]!=b'BSJB':raise ValueError('Invalid CLR metadata')
length=struct.unpack_from('<I',data,base+12)[0]
at=(base+16+length+3)&~3
_,count=struct.unpack_from('<HH',data,at);at+=4
streams={}
for _ in range(count):
    offset,size=struct.unpack_from('<II',data,at);at+=8
    end=data.index(0,at);name=data[at:end].decode();at=(end+4)&~3
    streams[name]=(base+offset,size)
start,size=streams['#US'];at=start+1;strings=[]
while at<start+size:
    token=at-start
    byte=data[at];at+=1
    if byte&0x80==0:n=byte
    elif byte&0xC0==0x80:n=((byte&0x3f)<<8)|data[at];at+=1
    else:n=((byte&0x1f)<<24)|int.from_bytes(data[at:at+3],'big');at+=3
    if not n:continue
    if at+n>start+size:raise ValueError('Invalid user string heap')
    text=data[at:at+n-1].decode('utf-16-le');at+=n
    strings.append({'token':token,'text':text})
Path(sys.argv[2]).write_text(json.dumps(strings,ensure_ascii=False,indent=2),encoding='utf-8')
print('Read-only CLR user strings:',len(strings))
