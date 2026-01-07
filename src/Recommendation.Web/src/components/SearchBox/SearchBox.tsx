import { useState, useEffect, useRef } from 'react';
import type { SearchResultDto } from '../../services/api';
import { api } from '../../services/api';
import './SearchBox.css';

interface SearchBoxProps {
  onProductSelect: (productId: number) => void;
  onCategorySelect: (categoryId: number, categoryName: string) => void;
  onBrandSelect: (brandId: number) => void;
}

export function SearchBox({ onProductSelect, onCategorySelect, onBrandSelect }: SearchBoxProps) {
  const [query, setQuery] = useState('');
  const [results, setResults] = useState<SearchResultDto | null>(null);
  const [isOpen, setIsOpen] = useState(false);
  const [loading, setLoading] = useState(false);
  const containerRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (query.length < 2) {
      setResults(null);
      setIsOpen(false);
      return;
    }

    const timeoutId = setTimeout(() => {
      setLoading(true);
      api.search(query, 15)
        .then(data => {
          setResults(data);
          setIsOpen(true);
        })
        .catch(console.error)
        .finally(() => setLoading(false));
    }, 300);

    return () => clearTimeout(timeoutId);
  }, [query]);

  useEffect(() => {
    function handleClickOutside(event: MouseEvent) {
      if (containerRef.current && !containerRef.current.contains(event.target as Node)) {
        setIsOpen(false);
      }
    }

    document.addEventListener('mousedown', handleClickOutside);
    return () => document.removeEventListener('mousedown', handleClickOutside);
  }, []);

  const handleClear = () => {
    setQuery('');
    setIsOpen(false);
    setResults(null);
  };

  const hasResults = results && (
    results.products.length > 0 || 
    results.categories.length > 0 || 
    results.brands.length > 0
  );

  return (
    <div className="search-box" ref={containerRef}>
      <div className="search-box__input-wrapper">
        <span className="search-box__icon">🔍</span>
        <input
          type="text"
          className="search-box__input"
          placeholder="Search products, categories, brands..."
          value={query}
          onChange={(e) => setQuery(e.target.value)}
          onFocus={() => results && setIsOpen(true)}
        />
        {loading && <span className="search-box__loading">⏳</span>}
      </div>

      {isOpen && (
        <div className="search-box__dropdown">
          {!hasResults && query.length >= 2 && !loading && (
            <div className="search-box__empty">No results found</div>
          )}

          {results?.products && results.products.length > 0 && (
            <div className="search-box__section">
              <div className="search-box__section-title">Products</div>
              {results.products.map(product => (
                <button
                  key={product.productId}
                  className="search-box__item"
                  onClick={() => {
                    handleClear();
                    onProductSelect(Number(product.productId));
                  }}
                >
                  <span className="search-box__item-icon">📦</span>
                  <div className="search-box__item-content">
                    <span className="search-box__item-name">{product.productName}</span>
                    {product.brandName && (
                      <span className="search-box__item-meta">{product.brandName}</span>
                    )}
                  </div>
                </button>
              ))}
            </div>
          )}

          {results?.categories && results.categories.length > 0 && (
            <div className="search-box__section">
              <div className="search-box__section-title">Categories</div>
              {results.categories.map(category => (
                <button
                  key={category.categoryId}
                  className="search-box__item"
                  onClick={() => {
                    handleClear();
                    onCategorySelect(Number(category.categoryId), category.categoryName);
                  }}
                >
                  <span className="search-box__item-icon">📁</span>
                  <div className="search-box__item-content">
                    <span className="search-box__item-name">{category.categoryName}</span>
                    {category.parentCategoryName && (
                      <span className="search-box__item-meta">in {category.parentCategoryName}</span>
                    )}
                  </div>
                </button>
              ))}
            </div>
          )}

          {results?.brands && results.brands.length > 0 && (
            <div className="search-box__section">
              <div className="search-box__section-title">Brands</div>
              {results.brands.map(brand => (
                <button
                  key={brand.brandId}
                  className="search-box__item"
                  onClick={() => {
                    handleClear();
                    onBrandSelect(Number(brand.brandId));
                  }}
                >
                  <span className="search-box__item-icon">🏷️</span>
                  <div className="search-box__item-content">
                    <span className="search-box__item-name">{brand.brandName}</span>
                    <span className="search-box__item-meta">{brand.productCount} products</span>
                  </div>
                </button>
              ))}
            </div>
          )}
        </div>
      )}
    </div>
  );
}
