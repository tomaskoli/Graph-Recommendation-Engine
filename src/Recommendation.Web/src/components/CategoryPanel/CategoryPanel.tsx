import { useEffect, useState } from 'react';
import type { CategoryDto } from '../../services/api';
import { api } from '../../services/api';
import './CategoryPanel.css';

interface CategoryPanelProps {
  onCategorySelect: (categoryId: number, categoryName: string) => void;
}

export function CategoryPanel({ onCategorySelect }: CategoryPanelProps) {
  const [categories, setCategories] = useState<CategoryDto[]>([]);
  const [expandedIds, setExpandedIds] = useState<Set<number>>(new Set());
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    api.getCategories()
      .then(setCategories)
      .catch(console.error)
      .finally(() => setLoading(false));
  }, []);

  const toggleExpand = (categoryId: number) => {
    setExpandedIds(prev => {
      const next = new Set(prev);
      if (next.has(categoryId)) {
        next.delete(categoryId);
      } else {
        next.add(categoryId);
      }
      return next;
    });
  };

  if (loading) {
    return <div className="category-panel category-panel--loading">Loading categories...</div>;
  }

  return (
    <div className="category-panel">
      <h2 className="category-panel__title">Browse Categories</h2>
      <div className="category-panel__list">
        {categories.map(category => (
          <CategoryItem
            key={category.categoryId}
            category={category}
            expandedIds={expandedIds}
            onToggle={toggleExpand}
            onSelect={onCategorySelect}
            depth={0}
          />
        ))}
      </div>
    </div>
  );
}

interface CategoryItemProps {
  category: CategoryDto;
  expandedIds: Set<number>;
  onToggle: (id: number) => void;
  onSelect: (id: number, name: string) => void;
  depth: number;
}

function CategoryItem({ category, expandedIds, onToggle, onSelect, depth }: CategoryItemProps) {
  const categoryId = Number(category.categoryId);
  const hasChildren = category.subCategories && category.subCategories.length > 0;
  const isExpanded = expandedIds.has(categoryId);

  return (
    <div className="category-item">
      <div 
        className={`category-item__row category-item__row--depth-${Math.min(depth, 3)}`}
        style={{ paddingLeft: `${0.75 + depth * 1}rem` }}
      >
        {hasChildren && (
          <button 
            className="category-item__toggle"
            onClick={() => onToggle(categoryId)}
            aria-label={isExpanded ? 'Collapse' : 'Expand'}
          >
            <span className={`category-item__arrow ${isExpanded ? 'category-item__arrow--expanded' : ''}`}>
              ▶
            </span>
          </button>
        )}
        {!hasChildren && <span className="category-item__spacer" />}
        <button 
          className="category-item__name"
          onClick={() => onSelect(categoryId, category.categoryName)}
        >
          {category.categoryName}
        </button>
        {hasChildren && (
          <span className="category-item__count">
            {category.subCategories!.length}
          </span>
        )}
      </div>
      {hasChildren && isExpanded && (
        <div className="category-item__children">
          {(category.subCategories as CategoryDto[]).map(child => (
            <CategoryItem
              key={child.categoryId}
              category={child}
              expandedIds={expandedIds}
              onToggle={onToggle}
              onSelect={onSelect}
              depth={depth + 1}
            />
          ))}
        </div>
      )}
    </div>
  );
}

