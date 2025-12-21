# Hierarchical Cave Network System

## Summary

A three-tier procedural cave generation system for voxel-based games. Guarantees continent-spanning connectivity while remaining fully deterministic and chunk-independent. Players can navigate from any cave to any other cave in the world by following the underground network.

The system layers three scales of cave infrastructure: rare deep highways spanning thousands of blocks, medium-frequency regional tunnels, and common local caves near the surface. Each tier connects to the one above it, creating a cohesive underground world.

CAVE GENERATION SPEC
====================

TIER DEFINITION
---------------
seed                    : int
node_size               : int (NxN chunks per node)
y_range                 : (min, max) world Y for cave placement

width                   : (min, max) tunnel width in blocks
height                  : (min, max) tunnel height in blocks  
border_radius           : (min, max) 0.0=rectangular, 1.0=ellipse

connections             : (min, max) per node
control_points          : (min, max) per curve
curve_wander            : float (how far control points stray from straight line)
curve_wander_vertical   : float (vertical wander multiplier, usually <1)


NODE
----
grid position           : (nx, nz) integers
world bounds            : node_pos * node_size * 16 ... + node_size * 16

anchor point:
    rng = seed(tier.seed, nx, nz)
    chunk_offset = rng.int2(0, node_size-1)
    block_offset = rng.int2(0, 15)
    y = rng.int(tier.y_range)
    
    anchor = (
        nx * node_size * 16 + chunk_offset.x * 16 + block_offset.x,
        y,
        nz * node_size * 16 + chunk_offset.z * 16 + block_offset.z
    )

junction radius:
    rng = seed(tier.seed, nx, nz)
    radius = max of all incoming tunnel widths * 1.5
    (or)
    radius = rng.float(tier.junction_radius_range)


CONNECTION SELECTION (must be symmetric)
----------------------------------------
for each node pair (A, B):
    canonical = (min(A,B), max(A,B))
    pair_rng = seed(tier.seed, "edge", canonical)
    
    edge_score = pair_rng.float(0, 1)

for each node N:
    node_rng = seed(tier.seed, "node", N)
    desired_count = node_rng.int(tier.connections)
    
    candidates = all neighbor nodes within reach
    
    for each candidate C:
        get edge_score(N, C)
    
    sort candidates by edge_score descending
    my_picks = top desired_count candidates

connection exists between A and B iff:
    A in B.my_picks AND B in A.my_picks
    
(both must independently choose each other)


CURVE GENERATION
----------------
given connection (A, B):
    canonical order: (A, B) = sorted
    curve_rng = seed(tier.seed, "curve", A, B)
    
    start = node(A).anchor
    end = node(B).anchor
    
    num_controls = curve_rng.int(tier.control_points)
    
    points = [start]
    
    for i in 1..num_controls:
        t = i / (num_controls + 1)
        
        base = lerp(start, end, t)
        
        max_offset = distance(start, end) * tier.curve_wander
        
        offset = (
            curve_rng.float(-max_offset, max_offset),
            curve_rng.float(-max_offset, max_offset) * tier.curve_wander_vertical,
            curve_rng.float(-max_offset, max_offset)
        )
        
        points.append(base + offset)
    
    points.append(end)
    
    return bezier(points)

curve properties:
    width = curve_rng.float(tier.width)
    height = curve_rng.float(tier.height)
    border_radius = curve_rng.float(tier.border_radius)


TUNNEL CROSS-SECTION
--------------------
at curve parameter t:
    center = curve.evaluate(t)
    tangent = curve.tangent(t)
    
    frame = orthonormal_frame(tangent)
        tangent = normalize(tangent)
        arbitrary_up = (0,1,0) unless tangent ≈ up, then (1,0,0)
        right = normalize(cross(tangent, arbitrary_up))
        up = cross(right, tangent)
    
    cross section in local (right, up) coords:
        superellipse: |x/hw|^e + |y/hh|^e <= 1
        where e = 2/border_radius (clamped)
        
        border_radius=1.0 → e=2 → ellipse
        border_radius=0.5 → e=4 → rounded rect
        border_radius→0   → e→∞ → rectangle


QUERY: is_cave(x, y, z)
-----------------------
point = (x, y, z)
node = world_to_node(x, z)

search_radius = max curve length in nodes + 1

for nx in (node.x - search_radius) .. (node.x + search_radius):
    for nz in (node.z - search_radius) .. (node.z + search_radius):
        
        check_node = (nx, nz)
        
        # check junction sphere
        if distance(point, node(check_node).anchor) < junction_radius:
            return true (or blend somehow)
        
        # check all outgoing connections
        for neighbor in connections(check_node):
            curve = get_curve(check_node, neighbor)
            
            # fast AABB reject
            if not curve.aabb.expanded(max_width).contains(point):
                continue
            
            # closest point on curve
            t, closest = curve.find_closest(point)
            
            # get frame at t
            frame = curve.frame_at(t)
            
            # to local coords
            local = frame.to_local(point - closest)
            
            # check cross-section
            if in_superellipse(local.y, local.z, width, height, border_radius):
                return true

return false


CHUNK GENERATION SHORTCUT
-------------------------
when generating chunk at (cx, cz):
    
    chunk_aabb = chunk world bounds
    
    # collect all curves that COULD intersect this chunk
    relevant_curves = []
    
    for each node within search_radius:
        for each connection from that node:
            if curve.aabb.expanded(max_width).intersects(chunk_aabb):
                relevant_curves.append(curve)
    
    # now for each voxel, only test relevant_curves
    # (or rasterize curves directly into chunk)


RASTERIZATION APPROACH (alternative to per-voxel query)
-------------------------------------------------------
for each relevant_curve:
    
    # adaptive subdivision
    subdivide curve until segment is < 1 block
    
    for each segment endpoint:
        center = segment midpoint
        tangent = segment direction
        frame = frame_at(center)
        
        # stamp cross-section
        for local_y in -height/2 .. height/2:
            for local_x in -width/2 .. width/2:
                if in_superellipse(local_x, local_y, ...):
                    world = frame.to_world(local_x, local_y) + center
                    set_voxel(world, AIR)


OPEN QUESTIONS
--------------
- junction blending: sphere? average of incoming frames? 
- vertical node stacking: 2D node grid or 3D?
- multi-tier interaction: do tiers connect to each other?
- dead ends: allow connections min=0?
- decorations: how to place features (water, ores) along curves?
