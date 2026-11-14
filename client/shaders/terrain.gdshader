shader_type canvas_item;
render_mode unshaded;

uniform vec2 atlas_grid = vec2(1.0);
varying vec2 tile_offset;

void vertex() {
	tile_offset = INSTANCE_CUSTOM.xy;
}

void fragment() {
	COLOR = texture(TEXTURE, (UV + tile_offset) / atlas_grid);
}
