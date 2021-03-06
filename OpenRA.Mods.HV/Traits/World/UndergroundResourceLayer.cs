#region Copyright & License Information
/*
 * Copyright 2007-2020 The OpenRA Developers (see AUTHORS)
 * This file is part of OpenRA, which is free software. It is made
 * available to you under the terms of the GNU General Public License
 * as published by the Free Software Foundation, either version 3 of
 * the License, or (at your option) any later version. For more
 * information, see COPYING.
 */
#endregion

using System;
using System.Linq;
using OpenRA.Graphics;
using OpenRA.Mods.Common.Traits;
using OpenRA.Traits;

namespace OpenRA.Mods.HV.Traits
{
	[Desc("Attach this to the world actor.", "Order of the layers defines the Z sorting.",
		"Resource density is based on the exact data stored in the map.")]
	public class UndergroundResourceLayerInfo : TraitInfo, IResourceLayerInfo, Requires<ResourceTypeInfo>
	{
		public override object Create(ActorInitializer init) { return new UndergroundResourceLayer(init.Self); }
	}

	public class UndergroundResourceLayer : IResourceLayer, IWorldLoaded
	{
		readonly World world;

		protected readonly CellLayer<ResourceLayerContents> Content;

		public bool IsResourceLayerEmpty { get { return resCells < 1; } }

		int resCells;

		public event Action<CPos, ResourceType> CellChanged;

		public UndergroundResourceLayer(Actor self)
		{
			world = self.World;

			Content = new CellLayer<ResourceLayerContents>(world.Map);
		}

		public void WorldLoaded(World w, WorldRenderer wr)
		{
			var resources = w.WorldActor.TraitsImplementing<ResourceType>()
				.ToDictionary(r => r.Info.ResourceType, r => r);

			foreach (var cell in w.Map.AllCells)
			{
				if (!resources.TryGetValue(w.Map.Resources[cell].Type, out var t))
					continue;

				if (!AllowResourceAt(t, cell))
					continue;

				Content[cell] = CreateResourceCell(t, cell);
			}

			foreach (var cell in w.Map.AllCells)
			{
				var type = GetResourceType(cell);
				if (type != null)
				{
					var temp = Content[cell];
					temp.Density = w.Map.Resources[cell].Index;

					Content[cell] = temp;
				}
			}
		}

		public bool AllowResourceAt(ResourceType rt, CPos cell)
		{
			if (!world.Map.Contains(cell))
				return false;

			if (!rt.Info.AllowedTerrainTypes.Contains(world.Map.GetTerrainInfo(cell).Type))
				return false;

			foreach (var a in world.ActorMap.GetActorsAt(cell))
			{
				if (!rt.Info.AllowUnderActors)
					return false;

				if (!rt.Info.AllowUnderBuildings && a.TraitOrDefault<Building>() != null)
					return false;
			}

			return rt.Info.AllowOnRamps || world.Map.Ramp[cell] == 0;
		}

		public bool CanSpawnResourceAt(ResourceType newResourceType, CPos cell)
		{
			if (!world.Map.Contains(cell))
				return false;

			var currentResourceType = GetResourceType(cell);
			return (currentResourceType == newResourceType && !IsFull(cell))
				|| (currentResourceType == null && AllowResourceAt(newResourceType, cell));
		}

		ResourceLayerContents CreateResourceCell(ResourceType t, CPos cell)
		{
			world.Map.CustomTerrain[cell] = world.Map.Rules.TerrainInfo.GetTerrainIndex(t.Info.TerrainType);
			++resCells;

			return new ResourceLayerContents
			{
				Type = t
			};
		}

		public void AddResource(ResourceType t, CPos p, int n)
		{
			var cell = Content[p];
			if (cell.Type == null)
				cell = CreateResourceCell(t, p);

			if (cell.Type != t)
				return;

			cell.Density = Math.Min(cell.Type.Info.MaxDensity, cell.Density + n);
			Content[p] = cell;

			CellChanged?.Invoke(p, cell.Type);
		}

		public bool IsFull(CPos cell)
		{
			var cellContents = Content[cell];
			return cellContents.Density == cellContents.Type.Info.MaxDensity;
		}

		public ResourceType Harvest(CPos cell)
		{
			var c = Content[cell];
			if (c.Type == null)
				return null;

			if (--c.Density < 0)
			{
				Content[cell] = ResourceLayerContents.Empty;
				world.Map.CustomTerrain[cell] = byte.MaxValue;
				--resCells;
			}
			else
				Content[cell] = c;

			CellChanged?.Invoke(cell, c.Type);

			return c.Type;
		}

		public void Destroy(CPos cell)
		{
			// Don't break other users of CustomTerrain if there are no resources
			var c = Content[cell];
			if (c.Type == null)
				return;

			--resCells;

			// Clear cell
			Content[cell] = ResourceLayerContents.Empty;
			world.Map.CustomTerrain[cell] = byte.MaxValue;

			CellChanged?.Invoke(cell, c.Type);
		}

		public ResourceType GetResourceType(CPos cell) { return Content[cell].Type; }

		public int GetResourceDensity(CPos cell) { return Content[cell].Density; }

		ResourceLayerContents IResourceLayer.GetResource(CPos cell) { return Content[cell]; }
		bool IResourceLayer.IsVisible(CPos cell) { return !world.FogObscures(cell); }
	}
}
