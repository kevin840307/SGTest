shader_type canvas_item;

// Reusable, asset-free background for private/missing original UI textures.
// 0 = celestial starfield, 1 = amber nebula, 2 = tactical grid.
uniform vec4 base_color : source_color = vec4(0.05,0.07,0.14,1.0);
uniform int motif = 0;

float noise2(vec2 p) {
	return fract(sin(dot(p,vec2(127.1,311.7)))*43758.5453123);
}

void fragment() {
	vec2 uv = UV;
	float radial = max(0.0,1.0-distance(uv,vec2(0.5,0.45))*1.1);
	vec3 color = base_color.rgb + radial*vec3(0.018,0.026,0.05);
	if (motif == 0) {
		float lane = exp(-28.0*pow(uv.y-(0.46+0.13*sin(uv.x*7.5)),2.0));
		color += lane*vec3(0.025,0.05,0.085);
		vec2 cells = floor(uv*vec2(260.0,150.0));
		float random_value = noise2(cells);
		float star = step(0.994,random_value)*(0.35+0.65*noise2(cells+47.0));
		color += star*vec3(0.4,0.7,1.0);
	} else if (motif == 1) {
		vec2 cells = floor(uv*vec2(190.0,120.0));
		float star = step(0.998,noise2(cells));
		color += star*vec3(0.8,0.44,0.18);
		color += 0.022*vec3(1.0,0.5,0.14)*exp(-12.0*pow(uv.y-0.55,2.0));
	} else {
		vec2 grid = abs(fract(uv*vec2(40.0,24.0))-0.5);
		float line = step(0.487,max(grid.x,grid.y));
		color += line*vec3(0.01,0.04,0.035);
		color += vec3(0.015,0.09,0.07)*exp(-30.0*abs(uv.x-0.16))*0.38;
	}
	COLOR = vec4(color,1.0);
}
