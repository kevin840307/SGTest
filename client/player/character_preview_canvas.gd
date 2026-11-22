extends Node2D
## Small auth/character-select paper-doll renderer.
## Unlike the world batch Canvas, previews have only a handful of parts, so
## Sprite2D layers use the original indexed HSL palette for hair and starter
## clothing without duplicating source atlases.

const PaletteHue = preload("res://player/palette_hue.gd")
const HUE_DEGREES := PaletteHue.HUE_DEGREES
const HAIR_SHADER_CODE := """
shader_type canvas_item;
uniform float hue_shift = 0.0;

vec3 rgb2hsl(vec3 c) {
	float mn=min(min(c.r,c.g),c.b);
	float mx=max(max(c.r,c.g),c.b);
	float l=(mx+mn)*0.5;
	float d=mx-mn;
	if (d < 0.00001) return vec3(0.0,0.0,l);
	float s=l>0.5 ? d/(2.0-mx-mn) : d/(mx+mn);
	float h=0.0;
	if (abs(mx-c.r)<0.00001) h=(c.g-c.b)/d+(c.g<c.b?6.0:0.0);
	else if (abs(mx-c.g)<0.00001) h=(c.b-c.r)/d+2.0;
	else h=(c.r-c.g)/d+4.0;
	return vec3(h/6.0,s,l);
}
float hue2rgb(float p,float q,float t) {
	t=fract(t);
	if(t<1.0/6.0) return p+(q-p)*6.0*t;
	if(t<0.5) return q;
	if(t<2.0/3.0) return p+(q-p)*(2.0/3.0-t)*6.0;
	return p;
}
vec3 hsl2rgb(vec3 v) {
	if(v.y<0.00001) return vec3(v.z);
	float q=v.z<0.5 ? v.z*(1.0+v.y) : v.z+v.y-v.z*v.y;
	float p=2.0*v.z-q;
	return vec3(hue2rgb(p,q,v.x+1.0/3.0),hue2rgb(p,q,v.x),hue2rgb(p,q,v.x-1.0/3.0));
}
void fragment() {
	vec4 c=texture(TEXTURE,UV);
	if(c.a<=0.0 || hue_shift==0.0) {
		COLOR=c;
	} else {
		vec3 hsl=rgb2hsl(c.rgb);
		hsl.x=fract(hsl.x+hue_shift/360.0);
		COLOR=vec4(hsl2rgb(hsl),c.a);
	}
}
"""

var _commands: Array = []
var _sprites: Array[Sprite2D] = []
var _hair_color := 0
var _hair_shader := Shader.new()
var _hair_material: ShaderMaterial = null
var _palette_materials: Dictionary = {}

func _init() -> void:
	_hair_shader.code = HAIR_SHADER_CODE
	_hair_material = _material_for(0)
	texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST

func set_hair_color(value: int) -> void:
	_hair_color = clampi(value,0,HUE_DEGREES.size()-1)
	_hair_material = _material_for(_hair_color)
	_apply_materials()

func _material_for(color_index: int) -> ShaderMaterial:
	var index := clampi(color_index,0,HUE_DEGREES.size()-1)
	if _palette_materials.has(index):
		return _palette_materials[index]
	var material := ShaderMaterial.new()
	material.shader = _hair_shader
	material.set_shader_parameter("hue_shift",HUE_DEGREES[index])
	_palette_materials[index] = material
	return material

func present(
		commands: Array, _ground_markers: Array = [],
		_combat_labels: Array = [], _player_labels: Array = []) -> void:
	_commands = commands
	_ensure_sprites(commands.size())
	for i in _sprites.size():
		var sprite := _sprites[i]
		if i >= commands.size():
			sprite.hide()
			continue
		var command: Dictionary = commands[i]
		var atlas := sprite.texture as AtlasTexture
		if atlas == null:
			atlas = AtlasTexture.new()
			# Build a valid AtlasTexture before assigning it to Sprite2D.
			# Assigning an empty AtlasTexture makes the headless renderer
			# allocate a null RID and report a leak at shutdown.
			atlas.atlas = command.texture
			atlas.region = command.uv
			sprite.texture = atlas
		else:
			atlas.atlas = command.texture
			atlas.region = command.uv
		var destination: Rect2 = command.destination
		sprite.position = destination.position
		sprite.scale = Vector2(-1.0,1.0) if destination.size.x < 0 else Vector2.ONE
		sprite.modulate = command.modulate
		sprite.set_meta("part_slot",int(command.get("slot",-1)))
		sprite.set_meta("palette_index",int(command.get("palette_index",0)))
		sprite.show()
	_apply_materials()

func clear() -> void:
	_commands = []
	for sprite in _sprites:
		sprite.hide()

func _ensure_sprites(count: int) -> void:
	while _sprites.size() < count:
		var sprite := Sprite2D.new()
		sprite.centered = false
		sprite.texture_filter = CanvasItem.TEXTURE_FILTER_NEAREST
		add_child(sprite)
		_sprites.append(sprite)

func _apply_materials() -> void:
	for sprite in _sprites:
		if not sprite.visible:
			continue
		var palette_index := int(sprite.get_meta("palette_index",0))
		sprite.material = _material_for(palette_index) if palette_index > 0 else null
