import { useEffect, useState } from 'react';
import type { ProductDto, ScoredProductDto, ProductDetailDto, RecommendationStrategy } from '../services/api';
import { api } from '../services/api';
import { RecommendationList } from '../components/RecommendationList';
import './ProductPage.css';

interface ProductPageProps {
  productId: number;
  onProductClick: (product: ProductDto) => void;
  onBack: () => void;
}

const STRATEGIES: { value: RecommendationStrategy; label: string }[] = [
  { value: 'hybrid', label: 'Hybrid' },
  { value: 'content', label: 'Content' },
  { value: 'behavioral', label: 'Behavioral' },
];

export function ProductPage({ productId, onProductClick, onBack }: ProductPageProps) {
  const [product, setProduct] = useState<ProductDetailDto | null>(null);
  const [similarProducts, setSimilarProducts] = useState<ScoredProductDto[]>([]);
  const [strategy, setStrategy] = useState<RecommendationStrategy>('hybrid');
  const [loading, setLoading] = useState(true);
  const [recommendationsLoading, setRecommendationsLoading] = useState(false);

  useEffect(() => {
    setLoading(true);
    setStrategy('hybrid');
    api.getProduct(productId)
      .then(productData => setProduct(productData as ProductDetailDto))
      .catch(console.error)
      .finally(() => setLoading(false));
  }, [productId]);

  useEffect(() => {
    setRecommendationsLoading(true);
    api.getRecommendations(productId, strategy)
      .then(recommendationsData => setSimilarProducts(recommendationsData.similarProducts.items))
      .catch(console.error)
      .finally(() => setRecommendationsLoading(false));
  }, [productId, strategy]);

  if (loading) {
    return <div className="product-page__loading">Loading...</div>;
  }

  if (!product) {
    return <div className="product-page__error">Product not found</div>;
  }

  const differentBrandProducts = similarProducts.filter(p => !p.sameBrand);

  return (
    <div className="product-page">
      <button className="product-page__back" onClick={onBack}>
        ← Back
      </button>

      <article className="product-page__main">
        <div className="product-page__ids">
          <span className="product-page__id-badge">Product ID: {product.productId}</span>
          {product.categoryId && <span className="product-page__id-badge">Category ID: {product.categoryId}</span>}
          {product.brandId && <span className="product-page__id-badge">Brand ID: {product.brandId}</span>}
        </div>
        <h1 className="product-page__title">{product.productName}</h1>
        {product.brandName && (
          <span className="product-page__brand">{product.brandName}</span>
        )}
        {product.productDescription && (
          <p className="product-page__description">{product.productDescription}</p>
        )}
      </article>

      {product.parameters && product.parameters.length > 0 && (
        <section className="product-page__parameters">
          <h2 className="product-page__section-title">Parameters</h2>
          <div className="product-page__parameters-grid">
            {product.parameters.map(param => (
              <div key={param.parameterId} className="product-page__parameter">
                <span className="product-page__parameter-name">{param.parameterName}</span>
                <span className="product-page__parameter-value">{param.value}</span>
              </div>
            ))}
          </div>
        </section>
      )}

      <div className="product-page__recommendations">
        <div className="product-page__strategy-selector">
          {STRATEGIES.map(s => (
            <button
              key={s.value}
              className={`product-page__strategy-button${strategy === s.value ? ' product-page__strategy-button--active' : ''}`}
              onClick={() => setStrategy(s.value)}
            >
              {s.label}
            </button>
          ))}
        </div>
        <div className={recommendationsLoading ? 'product-page__recommendations-loading' : undefined}>
          <RecommendationList
            title="Similar Products"
            products={similarProducts}
            onProductClick={onProductClick}
            showScore
          />
          <RecommendationList
            title="Similar Products from Different Brands"
            products={differentBrandProducts}
            onProductClick={onProductClick}
            showScore
          />
        </div>
      </div>
    </div>
  );
}
