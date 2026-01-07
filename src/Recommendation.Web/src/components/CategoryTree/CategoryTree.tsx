import type { CategoryDto } from '../../services/api';
import './CategoryTree.css';

interface CategoryTreeProps {
  categories: CategoryDto[];
  selectedId?: number;
  onSelect: (category: CategoryDto) => void;
}

export function CategoryTree({ categories, selectedId, onSelect }: CategoryTreeProps) {
  return (
    <nav className="category-tree">
      <h2 className="category-tree__title">Categories</h2>
      <ul className="category-tree__list">
        {categories.map(category => (
          <CategoryItem
            key={category.categoryId}
            category={category}
            selectedId={selectedId}
            onSelect={onSelect}
          />
        ))}
      </ul>
    </nav>
  );
}

interface CategoryItemProps {
  category: CategoryDto;
  selectedId?: number;
  onSelect: (category: CategoryDto) => void;
  depth?: number;
}

function CategoryItem({ category, selectedId, onSelect, depth = 0 }: CategoryItemProps) {
  const isSelected = category.categoryId === selectedId;
  const hasChildren = category.subCategories && category.subCategories.length > 0;

  return (
    <li className="category-tree__item">
      <button
        className={`category-tree__button ${isSelected ? 'category-tree__button--selected' : ''}`}
        style={{ paddingLeft: `${1 + depth * 0.75}rem` }}
        onClick={() => onSelect(category)}
      >
        <span className="category-tree__icon">{hasChildren ? '📁' : '📄'}</span>
        <span className="category-tree__name">{category.categoryName}</span>
      </button>
      {hasChildren && (
        <ul className="category-tree__sublist">
          {category.subCategories!.map(child => (
            <CategoryItem
              key={child.categoryId}
              category={child}
              selectedId={selectedId}
              onSelect={onSelect}
              depth={depth + 1}
            />
          ))}
        </ul>
      )}
    </li>
  );
}

