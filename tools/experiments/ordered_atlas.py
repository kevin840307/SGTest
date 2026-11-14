"""Readable direction panels containing action rows in source frame order.

No frame rotation. Trimming is preserved, and only row heights are shared;
optional uniform cells permit a direct density/readability comparison.
"""
from collections import defaultdict
from math import ceil
import re

ANIMATION=re.compile(r'^(.+)_([A-Z]{2})([1-8])$',re.I)
ACTION_ORDER=['NS','NW','NR','FA','AA','NH','ND']

def action_key(clip):
    match=ANIMATION.match(clip)
    code=match[2].upper() if match else clip
    return (ACTION_ORDER.index(code) if code in ACTION_ORDER else len(ACTION_ORDER),code,clip)

def panel(images,clips,width,grid):
    placements={};regions={};x=0;y=14;row_height=0;used_width=0
    for clip,slots in sorted(clips.items(),key=lambda entry:action_key(entry[0])):
        if not slots:continue
        visible=[images[i] for i in slots if i is not None]
        cell_w=max((image.width+2 for image in visible),default=3)
        cell_h=max((image.height+2 for image in visible),default=3)
        if cell_w>width:raise ValueError('frame wider than direction panel')
        rows=[]
        if grid:
            columns=max(1,width//cell_w)
            for start in range(0,len(slots),columns):
                rows.append([(index,slots[index],cell_w,cell_h) for index in range(start,min(start+columns,len(slots)))])
        else:
            row=[];used=0
            for index,image_id in enumerate(slots):
                w=images[image_id].width+2 if image_id is not None else cell_w
                h=images[image_id].height+2 if image_id is not None else cell_h
                if row and used+w>width:
                    rows.append(row);row=[];used=0
                row.append((index,image_id,w,h));used+=w
            if row:rows.append(row)
        action_width=max(sum(entry[2] for entry in row) for row in rows)
        action_height=14+sum(max(entry[3] for entry in row) for row in rows)
        if x and x+action_width>width:x=0;y+=row_height+2;row_height=0
        top=y;local_y=y+14;cells=[None]*len(slots);row_indices=[]
        for row in rows:
            height=max(entry[3] for entry in row);local_x=x
            row_indices.append([entry[0] for entry in row])
            for index,image_id,w,h in row:
                cells[index]=[local_x,local_y,w,height]
                if image_id is not None:
                    image=images[image_id]
                    placements[image_id]=(local_x+(w-image.width)//2,local_y+height-image.height-1,image.width,image.height)
                local_x+=w
            local_y+=height
        regions[clip]={'rect':[x,top,action_width,action_height],
                       'header':[x,top,action_width,14],
                       'cells':cells,'rows':row_indices,'frame_count':len(slots)}
        used_width=max(used_width,x+action_width)
        x+=action_width+2;row_height=max(row_height,action_height)
    return placements,regions,(used_width,y+row_height)


def place_panels(panels,columns,side,separate):
    boxes={};sizes=[]
    lane=max((size[0]+2 for _,_,size in panels.values()),default=3)
    if lane*columns>side:raise ValueError('direction columns exceed texture width')
    heights=[]
    for direction,(_,_,(w,h)) in panels.items():
        if w+2>side or h+2>side:raise ValueError('direction panel exceeds texture')
        if separate:
            page=len(sizes);boxes[direction]=(page,1,1,w,h);sizes.append([w+2,h+2]);continue
        candidates=[(ys[col],page,col) for page,ys in enumerate(heights) for col in range(columns) if ys[col]+h+2<=side]
        if not candidates:
            heights.append([0]*columns);sizes.append([0,0]);candidates=[(0,len(heights)-1,0)]
        y,page,col=min(candidates);x=col*lane
        boxes[direction]=(page,x+1,y+1,w,h)
        sizes[page]=[max(sizes[page][0],x+w+2),max(sizes[page][1],y+h+2)]
        heights[page][col]+=h+2
    return boxes,sizes

def ordered_layout(images,clip_images,side,separate=False,grid=False):
    directions=defaultdict(dict)
    for clip,slots in clip_images.items():
        match=ANIMATION.match(clip)
        directions[match[3] if match else 'default'][clip]=slots
    directions=dict(sorted(directions.items()))
    maximum=max((image.width+2 for image in images),default=3)
    widths=sorted({min(side-2,w) for w in [maximum,128,192,256,384,512,768,1024,1536,2048,3072,4096,8192,16384] if w>=maximum})
    candidates=[]
    for width in widths:
        try:
            panels={key:panel(images,clips,width,grid) for key,clips in directions.items()}
            widest=max((size[0]+2 for _,_,size in panels.values()),default=3)
            column_counts=[1] if separate else range(1,min(len(panels),side//widest)+1)
            for columns in column_counts:
                boxes,sizes=place_panels(panels,columns,side,separate)
                # Single-source pictures keep their natural ratio. Animation
                # sheets use horizontal pages, without rotating frame pixels.
                if sum(len(slots) for clips in directions.values() for slots in clips.values())>1:
                    sizes=[[max(w,h),h] for w,h in sizes]
                candidates.append((len(sizes),sum(w*h for w,h in sizes),panels,boxes,sizes))
        except ValueError:continue
    if not candidates:raise ValueError('ordered direction/action blocks exceed texture')
    _,_,panels,boxes,sizes=min(candidates,key=lambda row:row[:2])
    placements={};direction_regions={};clip_regions={}
    for direction,(local,actions,_) in panels.items():
        page,bx,by,w,h=boxes[direction]
        direction_regions[direction]={'page':page,'rect':[bx,by,w,h]}
        for index,(x,y,fw,fh) in local.items():placements[index]=(page,bx+x,by+y,fw,fh)
        for clip,region in actions.items():
            x,y,rw,rh=region['rect']
            clip_regions[clip]={**region,'page':page,'direction':direction,'rect':[bx+x,by+y,rw,rh],
                                'header':[bx+region['header'][0],by+region['header'][1],region['header'][2],region['header'][3]],
                                'cells':[[bx+x,by+y,cw,ch] for x,y,cw,ch in region['cells']]}
    return placements,sizes,direction_regions,clip_regions
