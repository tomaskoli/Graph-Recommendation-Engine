/* generated using openapi-typescript-codegen -- do not edit */
/* istanbul ignore file */
/* tslint:disable */
/* eslint-disable */
import type { ProductDto } from './ProductDto';
export type PaginatedResultOfProductDto = {
    items: Array<ProductDto>;
    totalCount: number | string;
    page: number | string;
    pageSize: number | string;
    totalPages: number | string;
    hasMore: boolean;
};

