using Unity.Entities;
using TogetherWeFall.Equipment;

namespace TogetherWeFall.Inventory
{
    /// <summary>
    /// Everything that knows how a rectangle sits in a grid.
    ///
    /// A struct of static methods rather than a system, because two different
    /// owners need the same answers and neither of them is the other's caller.
    /// InventoryPlacementSystem applies what a client asked for; EquipmentSystem
    /// moves an item between a slot and the bag as one indivisible step of its
    /// own decision; and the UI asks the read-only half of this while a drag is
    /// in flight, to colour the square under the cursor without writing
    /// anything. Three callers, one set of rules, one file to be wrong in.
    ///
    /// Cost is O(width × height) for a single item, never O(grid) — with the one
    /// exception of Clear, and the comment there says why that is the right
    /// trade.
    /// </summary>
    public struct GridFit
    {
        /// <summary>
        /// How many cells an item covers, honouring rotation.
        ///
        /// Rotation is a swap of the two numbers and nothing else. That is the
        /// whole reason the deferred "non-rectangular shapes" idea is deferred:
        /// the moment a shape is not a rectangle, this stops being two ints and
        /// becomes a mask, and every method below changes with it.
        /// </summary>
        public static bool TryGetFootprint(
            ItemDatabase items, int itemId, bool rotated, out int width, out int height)
        {
            width = 0;
            height = 0;

            int index = items.IndexOf(itemId);
            if (index < 0)
                return false;

            // By reference: ItemBlob carries a BlobArray of affixes, and copying
            // the struct leaves that array pointing at nothing useful.
            ref ItemBlob item = ref items.Value.Value.Items[index];

            width = rotated ? item.GridHeight : item.GridWidth;
            height = rotated ? item.GridWidth : item.GridHeight;

            return width > 0 && height > 0;
        }

        /// <summary>Whether this item is allowed to be turned on its side.</summary>
        public static bool CanRotate(ItemDatabase items, int itemId)
        {
            int index = items.IndexOf(itemId);
            if (index < 0)
                return false;

            ref ItemBlob item = ref items.Value.Value.Items[index];
            return item.CanRotate;
        }

        /// <summary>
        /// Whether a rectangle of this size fits with its top-left corner here.
        ///
        /// `ignore` is the item being moved. Without it, dragging an item one
        /// cell to the right would collide with the copy of itself that is still
        /// written into the cells it currently covers — and every move would be
        /// refused for being blocked by the thing doing the moving.
        /// </summary>
        public static bool Fits(
            DynamicBuffer<InventoryCell> cells,
            in InventoryGridComponent grid,
            int originX,
            int originY,
            int width,
            int height,
            Entity ignore)
        {
            if (originX < 0 || originY < 0)
                return false;

            if (originX + width > grid.Width || originY + height > grid.Height)
                return false;

            for (int y = originY; y < originY + height; y++)
            {
                for (int x = originX; x < originX + width; x++)
                {
                    Entity occupant = cells[grid.IndexOf(x, y)].OccupyingItem;

                    if (occupant != Entity.Null && occupant != ignore)
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// The first free rectangle, scanning left to right and top to bottom.
        ///
        /// First-fit rather than best-fit on purpose. Best-fit would pack more in
        /// but would also move things around in ways the player did not ask for,
        /// and an inventory that rearranges itself is worse than one that fills
        /// up — that is what the deferred one-click sort is for.
        /// </summary>
        public static bool FindFirstFit(
            DynamicBuffer<InventoryCell> cells,
            in InventoryGridComponent grid,
            int width,
            int height,
            Entity ignore,
            out int originX,
            out int originY)
        {
            originX = 0;
            originY = 0;

            if (width <= 0 || height <= 0 || width > grid.Width || height > grid.Height)
                return false;

            for (int y = 0; y <= grid.Height - height; y++)
            {
                for (int x = 0; x <= grid.Width - width; x++)
                {
                    if (!Fits(cells, grid, x, y, width, height, ignore))
                        continue;

                    originX = x;
                    originY = y;
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Writes the item into every cell it covers.
        ///
        /// Never call this without a Fits that returned true first — it does not
        /// check, deliberately, so that the check and the write cannot disagree
        /// about what "here" means.
        /// </summary>
        public static void Occupy(
            DynamicBuffer<InventoryCell> cells,
            in InventoryGridComponent grid,
            int originX,
            int originY,
            int width,
            int height,
            Entity item)
        {
            for (int y = originY; y < originY + height; y++)
            {
                for (int x = originX; x < originX + width; x++)
                    cells[grid.IndexOf(x, y)] = new InventoryCell { OccupyingItem = item };
            }
        }

        /// <summary>
        /// Erases an item from the grid, and returns how many cells it held.
        ///
        /// A scan of the whole buffer rather than a walk of the footprint the
        /// placement says the item has. Sixty comparisons is nothing, and it
        /// makes this the one operation that cannot leave a stale cell behind:
        /// walking the recorded footprint would trust ItemGridPlacement to be
        /// right, and the entire point of erasing is that it might not be.
        /// </summary>
        public static int Clear(DynamicBuffer<InventoryCell> cells, Entity item)
        {
            if (item == Entity.Null)
                return 0;

            int cleared = 0;

            for (int i = 0; i < cells.Length; i++)
            {
                if (cells[i].OccupyingItem != item)
                    continue;

                cells[i] = new InventoryCell { OccupyingItem = Entity.Null };
                cleared++;
            }

            return cleared;
        }

        /// <summary>
        /// Grows a cell buffer to the size its grid claims, filling with empty.
        ///
        /// Called once when a container is created. A buffer shorter than
        /// Width × Height would index out of bounds on the first placement, and
        /// a longer one would hide cells nothing can ever reach.
        /// </summary>
        public static void Reset(
            DynamicBuffer<InventoryCell> cells, in InventoryGridComponent grid)
        {
            cells.Clear();
            cells.Capacity = grid.CellCount;

            for (int i = 0; i < grid.CellCount; i++)
                cells.Add(new InventoryCell { OccupyingItem = Entity.Null });
        }
    }
}
