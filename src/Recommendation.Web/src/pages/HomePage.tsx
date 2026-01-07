import { useEffect, useState } from 'react';
import type { ProductDto } from '../services/api';
import { api } from '../services/api';
import { ProductCard } from '../components/ProductCard';
import { CategoryPanel } from '../components/CategoryPanel';
import type { RecentProduct } from '../hooks/useRecentProducts';
import './HomePage.css';

interface HomePageProps {
  onProductClick: (product: ProductDto) => void;
  onCategorySelect: (categoryId: number, categoryName: string) => void;
  onBack?: () => void;
  selectedCategoryId?: number;
  selectedCategoryName?: string;
  recentProducts?: RecentProduct[];
}

export function HomePage({ onProductClick, onCategorySelect, onBack, selectedCategoryId, selectedCategoryName, recentProducts = [] }: HomePageProps) {
  const [products, setProducts] = useState<ProductDto[]>([]);
  const [loading, setLoading] = useState(false);

  useEffect(() => {
    if (!selectedCategoryId) {
      setProducts([]);
      return;
    }

    setLoading(true);
    api.getProductsByCategory(selectedCategoryId)
      .then(setProducts)
      .catch(console.error)
      .finally(() => setLoading(false));
  }, [selectedCategoryId]);

  if (!selectedCategoryId) {
    return (
      <div className="home-page">
        {recentProducts.length > 0 && (
          <section className="home-page__recent">
            <h2 className="home-page__section-title">Recently Viewed</h2>
            <div className="home-page__recent-grid">
              {recentProducts.map(product => (
                <ProductCard
                  key={product.productId}
                  product={{
                    productId: product.productId,
                    productName: product.productName,
                    brandName: product.brandName ?? null,
                    brandId: product.brandId ?? null,
                    categoryId: product.categoryId ?? null,
                    productDescription: null,
                  }}
                  onClick={() => onProductClick({
                    productId: product.productId,
                    productName: product.productName,
                    brandName: product.brandName ?? null,
                    brandId: product.brandId ?? null,
                    categoryId: product.categoryId ?? null,
                    productDescription: null,
                  })}
                />
              ))}
            </div>
          </section>
        )}
        <CategoryPanel onCategorySelect={onCategorySelect} />
      </div>
    );
  }

  return (
    <div className="home-page">
      {onBack && (
        <button className="home-page__back" onClick={onBack}>
          ← Back
        </button>
      )}
      <header className="home-page__header">
        <h1>{selectedCategoryName || 'Category'}</h1>
        <span className="home-page__count">{products.length} products</span>
      </header>
            {loading ? (
              <div className="home-page__loading">Loading...</div>
            ) : products.length === 0 ? (
              <div className="home-page__empty">No products in this category</div>
            ) : (
              <div className="home-page__grid">
                {products.map(product => (
                  <ProductCard
                    key={product.productId}
                    product={product}
                    onClick={() => onProductClick(product)}
                  />
                ))}
              </div>
            )}
    </div>
  );
}
