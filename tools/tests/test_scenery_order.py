"""Synthetic original front boundaries, same-Y stability and bounded swaps."""
import unittest
import sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from asset_converter.source.scenery import point_in_front, swap_required, compile_scenery_order


class SceneryOrderTests(unittest.TestCase):
    def test_front_boundary_chart_is_camera_independent(self):
        # With edges (-20,-10)/(20,-10), the middle joins at (0,0).
        position = [100,200]
        for x in range(-40,41):
            boundary = -min(abs(x),20)/2
            for y in range(-32,4):
                self.assertEqual(point_in_front(position,[-20,-10,20,-10],[100+x,200+y]),y>boundary)
        self.assertFalse(point_in_front([0,0],[-20,-10,20,-10],[0,0]))

    def test_sort_predicate_is_not_a_total_order_and_hits_original_cap(self):
        a = {'p':[-20,-10],'sorting_mode':0,'front':[-30,-5,30,-5]}
        b = {'p':[0,0],'sorting_mode':0,'front':[-30,-20,30,-5]}
        self.assertTrue(swap_required(a,b))
        self.assertTrue(swap_required(b,a))
        placements = [{'p':row['p'],'parent_index':None,'definition':str(i)} for i,row in enumerate([a,b])]
        definitions = {str(i):{**row,'draw_group':1} for i,row in enumerate([a,b])}
        order = compile_scenery_order(placements,definitions)
        self.assertEqual(order['passes'],[1,1025,1])
        self.assertEqual(order['settled'],[True,False,True])
        self.assertEqual(order['groups'],[[],[1,0],[]])

    def test_same_y_keeps_input_order_children_excluded_and_groups_separate(self):
        definitions = {str(i):{'sorting_mode':1,'draw_group':g} for i,g in enumerate([0,1,2])}
        placements = [
            {'p':[30,50],'parent_index':None,'definition':'1'},
            {'p':[10,50],'parent_index':None,'definition':'1'},
            {'p':[0,0],'parent_index':0,'definition':'1'},
            {'p':[0,100],'parent_index':None,'definition':'0'},
            {'p':[0,0],'parent_index':None,'definition':'2'},
        ]
        self.assertEqual(compile_scenery_order(placements,definitions)['groups'],[[3],[0,1],[4]])
        building = {'p':[0,0],'sorting_mode':0,'front':[-20,-10,20,-10]}
        behind = {'p':[0,0],'sorting_mode':1}
        ahead = {'p':[0,1],'sorting_mode':1}
        self.assertTrue(swap_required(building,behind))
        self.assertFalse(swap_required(building,ahead))
        self.assertFalse(swap_required(behind,building))
        self.assertTrue(swap_required(ahead,building))
