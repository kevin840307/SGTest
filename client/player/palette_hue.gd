extends RefCounted
## Original SGO indexed palette hue policy reconstructed from Recovered.
## World batch rendering uses this only to lazily bake a shared texture variant;
## auth previews use the equivalent GPU shader.
const HUE_DEGREES := [0.0,90.0,180.0,270.0,45.0,135.0,225.0,315.0]

static func valid_index(value: int) -> bool:
	return value >= 0 and value < HUE_DEGREES.size()

static func hue_degrees(value: int) -> float:
	return HUE_DEGREES[value] if valid_index(value) else 0.0

static func shift_texture(source: Texture2D, color_index: int) -> Texture2D:
	if source == null or not valid_index(color_index) or color_index == 0:
		return source
	var image := source.get_image()
	if image == null or image.is_empty():
		return source
	var shifted := shift_image(image,color_index)
	return ImageTexture.create_from_image(shifted) if shifted != null and not shifted.is_empty() else source

static func shift_image(source: Image, color_index: int) -> Image:
	if source == null or source.is_empty():
		return Image.new()
	var image := source.duplicate()
	if not valid_index(color_index) or color_index == 0:
		return image
	if image.is_compressed():
		var error: Error = image.decompress()
		if error != OK:
			return Image.new()
	image.convert(Image.FORMAT_RGBA8)
	var bytes: PackedByteArray = image.get_data()
	var hue_delta := hue_degrees(color_index)/360.0
	for offset in range(0,bytes.size(),4):
		if bytes[offset+3] == 0:
			continue
		var r := float(bytes[offset])/255.0
		var g := float(bytes[offset+1])/255.0
		var b := float(bytes[offset+2])/255.0
		var mn := minf(r,minf(g,b))
		var mx := maxf(r,maxf(g,b))
		var lightness := (mx+mn)*0.5
		var delta := mx-mn
		if delta < 0.00001:
			continue
		var saturation := (
			delta/(2.0-mx-mn)
			if lightness > 0.5
			else delta/(mx+mn))
		var hue := 0.0
		if absf(mx-r) < 0.00001:
			hue=(g-b)/delta+(6.0 if g<b else 0.0)
		elif absf(mx-g) < 0.00001:
			hue=(b-r)/delta+2.0
		else:
			hue=(r-g)/delta+4.0
		hue=fposmod(hue/6.0+hue_delta,1.0)
		var q := (
			lightness*(1.0+saturation)
			if lightness < 0.5
			else lightness+saturation-lightness*saturation)
		var p := 2.0*lightness-q
		bytes[offset]=clampi(roundi(_hue_to_rgb(p,q,hue+1.0/3.0)*255.0),0,255)
		bytes[offset+1]=clampi(roundi(_hue_to_rgb(p,q,hue)*255.0),0,255)
		bytes[offset+2]=clampi(roundi(_hue_to_rgb(p,q,hue-1.0/3.0)*255.0),0,255)
	return Image.create_from_data(image.get_width(),image.get_height(),false,Image.FORMAT_RGBA8,bytes)

static func _hue_to_rgb(p: float, q: float, t: float) -> float:
	t=fposmod(t,1.0)
	if t < 1.0/6.0:
		return p+(q-p)*6.0*t
	if t < 0.5:
		return q
	if t < 2.0/3.0:
		return p+(q-p)*(2.0/3.0-t)*6.0
	return p
